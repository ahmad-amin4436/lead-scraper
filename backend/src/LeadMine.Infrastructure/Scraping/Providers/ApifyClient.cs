using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Scraping.Providers;

/// <summary>
/// Low-level "run an actor, wait for it, fetch the results" mechanics shared by
/// every Apify-backed data source in this app: Google Maps discovery
/// (<see cref="ApifyGoogleMapsProvider"/>), Google Maps enrichment, LinkedIn
/// company enrichment, and LinkedIn people search.
/// <para>
/// Each caller supplies its own actor id and input, and gets back raw dataset
/// items as <see cref="JsonElement"/> to deserialize into whatever shape that
/// actor returns — this class owns only what every one of them needs
/// identically: trying each configured token in turn, polling run status, and
/// retrieving the dataset once it succeeds.
/// </para>
/// </summary>
public sealed class ApifyClient(IHttpClientFactory httpClientFactory, ILogger<ApifyClient> logger)
{
    private const string ApiBase = "https://api.apify.com/v2";

    /// <summary>How often to poll a running actor for completion.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? Readiness(ScraperOptions options, string dataSourceLabel) =>
        options.ApifyApiTokens.Any(t => !string.IsNullOrWhiteSpace(t))
            ? null
            : $"{dataSourceLabel} needs at least one Apify API token. Set Scraper:ApifyApiTokens.";

    /// <summary>
    /// Runs <paramref name="actorId"/> with <paramref name="input"/>, trying each
    /// configured token in order — a token that is rate-limited or rejected is
    /// skipped in favour of the next one, so one exhausted or revoked Apify
    /// account does not take the whole data source down.
    /// </summary>
    public async Task<List<JsonElement>> RunAsync(
        string actorId,
        object input,
        int maxItems,
        ProviderContext context,
        CancellationToken ct)
    {
        var tokens = context.Options.ApifyApiTokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

        if (tokens.Count == 0)
        {
            throw new ProviderException(
                $"No Apify API token configured for actor \"{actorId}\".", ProviderFailure.MissingApiKey);
        }

        var client = httpClientFactory.CreateClient(ScraperHttpClients.Apify);
        ProviderException? lastError = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var isLastToken = i == tokens.Count - 1;

            await context.RateLimiter.WaitAsync(ct);

            try
            {
                var runId = await StartRunAsync(client, actorId, token, input, context, ct);
                var datasetId = await PollUntilFinishedAsync(client, runId, token, actorId, context, ct);
                return await FetchResultsAsync(client, datasetId, token, maxItems, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ProviderException ex) when (
                !isLastToken && ex.Failure is ProviderFailure.QuotaExceeded or ProviderFailure.InvalidApiKey)
            {
                logger.LogWarning(
                    ex, "Apify token {Index}/{Total} failed ({Failure}) calling {ActorId}; trying the next configured token",
                    i + 1, tokens.Count, ex.Failure, actorId);

                lastError = ex;
            }
        }

        throw lastError ?? new ProviderException($"All configured Apify tokens failed calling {actorId}.");
    }

    private async Task<string> StartRunAsync(
        HttpClient client,
        string actorId,
        string token,
        object input,
        ProviderContext context,
        CancellationToken ct)
    {
        using var deadline = ScraperHttpClients.Deadline(context.Options.RequestTimeoutMs, ct);

        HttpResponseMessage response;

        try
        {
            response = await ScraperRetry.ExecuteAsync(
                async _ =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/acts/{actorId}/runs")
                    {
                        Content = JsonContent.Create(input, options: JsonOptions),
                    };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    var result = await client.SendAsync(request, deadline.Token);

                    if (result.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var retryAfter = ScraperRetry.ParseRetryAfter(result);
                        result.Dispose();
                        throw new RateLimitedException("Apify returned 429", retryAfter);
                    }

                    if (ScraperRetry.IsRetryableStatus(result.StatusCode))
                    {
                        result.Dispose();
                        throw new HttpRequestException($"Apify returned {(int)result.StatusCode}", null, result.StatusCode);
                    }

                    return result;
                },
                context.Options.RetryAttempts,
                logger,
                ct,
                context.RateLimiter);
        }
        catch (RateLimitedException ex)
        {
            throw new ProviderException($"Apify rate limit exceeded calling {actorId}.", ProviderFailure.QuotaExceeded, ex);
        }

        using var responseScope = response;

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ProviderException(
                $"Apify rejected the API token for actor \"{actorId}\". Check that it is valid and the actor is accessible to this account.",
                ProviderFailure.InvalidApiKey);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ProviderException($"Apify actor \"{actorId}\" was not found.", ProviderFailure.InvalidApiKey);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new ProviderException($"Apify error starting the run: HTTP {(int)response.StatusCode} — {Truncate(body)}");
        }

        var envelope = await response.Content.ReadFromJsonAsync<ApifyRunEnvelope>(JsonOptions, ct);
        var runId = envelope?.Data?.Id;

        if (string.IsNullOrEmpty(runId))
        {
            throw new ProviderException($"Apify did not return a run id for actor \"{actorId}\".");
        }

        return runId;
    }

    private async Task<string> PollUntilFinishedAsync(
        HttpClient client,
        string runId,
        string token,
        string actorId,
        ProviderContext context,
        CancellationToken ct)
    {
        var deadlineAt = DateTimeOffset.UtcNow.AddMilliseconds(context.Options.ApifyRunTimeoutMs);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (DateTimeOffset.UtcNow >= deadlineAt)
            {
                throw new ProviderException(
                    $"Apify run {runId} ({actorId}) did not finish within {context.Options.ApifyRunTimeoutMs / 1000}s.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/actor-runs/{runId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var deadline = ScraperHttpClients.Deadline(context.Options.RequestTimeoutMs, ct);
            using var response = await ScraperRetry.ExecuteAsync(
                _ => client.SendAsync(request, deadline.Token),
                context.Options.RetryAttempts,
                logger,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException($"Apify error checking run status: HTTP {(int)response.StatusCode}");
            }

            var envelope = await response.Content.ReadFromJsonAsync<ApifyRunEnvelope>(JsonOptions, ct);
            var status = envelope?.Data?.Status;
            var datasetId = envelope?.Data?.DefaultDatasetId;

            switch (status)
            {
                case "SUCCEEDED":
                    if (string.IsNullOrEmpty(datasetId))
                    {
                        throw new ProviderException($"Apify run {runId} ({actorId}) succeeded but returned no dataset.");
                    }

                    return datasetId;

                case "FAILED" or "TIMED-OUT" or "ABORTED":
                    throw new ProviderException($"Apify run {runId} ({actorId}) ended with status {status}.");

                default:
                    // READY, RUNNING, TIMING-OUT, ABORTING — still in flight.
                    await Task.Delay(PollInterval, ct);
                    break;
            }
        }
    }

    private async Task<List<JsonElement>> FetchResultsAsync(
        HttpClient client,
        string datasetId,
        string token,
        int maxItems,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiBase}/datasets/{datasetId}/items?clean=1&limit={Math.Max(1, maxItems)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderException($"Apify error fetching results: HTTP {(int)response.StatusCode}");
        }

        return await response.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOptions, ct) ?? [];
    }

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300];

    private sealed class ApifyRunEnvelope
    {
        [JsonPropertyName("data")] public ApifyRunData? Data { get; set; }
    }

    private sealed class ApifyRunData
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("defaultDatasetId")] public string? DefaultDatasetId { get; set; }
    }
}
