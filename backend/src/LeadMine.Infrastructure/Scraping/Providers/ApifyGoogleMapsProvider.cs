using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeadMine.Application.DTOs;
using LeadMine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Scraping.Providers;

/// <summary>
/// Google Maps, scraped by an Apify actor rather than called directly.
/// <para>
/// Google Maps itself is a JS-rendered app with no public search API and active
/// bot defenses — scraping it directly would need a headless browser this host
/// cannot run, and would violate Google's terms regardless of where that
/// browser ran. Apify is a legitimate, paid third-party platform whose declared
/// product is exactly this: it operates its own scraping infrastructure and
/// actors (e.g. the Google Maps Scraper) as a service, so the detection-evasion
/// and maintenance burden is Apify's problem, not this codebase's.
/// </para>
/// <para>
/// Deliberately does not enable the actor's own <c>scrapeContacts</c> option
/// (which crawls each business's website for email/social links) — that is a
/// separate billed event per Apify's pricing, and this app already has a
/// robots.txt-respecting crawler (<see cref="Enrichment.EnrichmentService"/>)
/// that does the same job for free as part of the normal pipeline every other
/// provider's results already go through.
/// </para>
/// <para>
/// Supports multiple tokens (<see cref="ScraperOptions.ApifyApiTokens"/>),
/// tried in order: a token that is rate-limited or rejected is skipped in
/// favour of the next configured one, so one exhausted or revoked Apify
/// account does not take the whole data source down for the rest of a run.
/// </para>
/// </summary>
public sealed class ApifyGoogleMapsProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<ApifyGoogleMapsProvider> logger) : IPlaceProvider
{
    private const string ApiBase = "https://api.apify.com/v2";

    /// <summary>How often to poll a running actor for completion.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public BusinessSource Source => BusinessSource.Apify;

    public string Label => "Google Maps (Apify)";

    public string? Readiness(ProviderContext context) =>
        context.Options.ApifyApiTokens.Any(t => !string.IsNullOrWhiteSpace(t))
            ? null
            : "Google Maps (Apify) needs at least one API token. Set Scraper:ApifyApiTokens.";

    public async Task<IReadOnlyList<ProviderBusiness>> SearchAsync(
        ProviderQuery query,
        ProviderContext context,
        CancellationToken ct = default)
    {
        var notReady = Readiness(context);
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        var tokens = context.Options.ApifyApiTokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var actorId = string.IsNullOrWhiteSpace(context.Options.ApifyActorId)
            ? "compass~crawler-google-places"
            : context.Options.ApifyActorId;

        var client = httpClientFactory.CreateClient(ScraperHttpClients.Apify);

        ProviderException? lastError = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var isLastToken = i == tokens.Count - 1;

            await context.RateLimiter.WaitAsync(ct);

            try
            {
                var runId = await StartRunAsync(client, actorId, token, query, context, ct);
                var datasetId = await PollUntilFinishedAsync(client, runId, token, context, ct);
                return await FetchResultsAsync(client, datasetId, token, query, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ProviderException ex) when (
                !isLastToken && ex.Failure is ProviderFailure.QuotaExceeded or ProviderFailure.InvalidApiKey)
            {
                // This token is down; the next one gets a clean attempt rather
                // than failing the whole task over one exhausted account.
                logger.LogWarning(
                    ex, "Apify token {Index}/{Total} failed ({Failure}); trying the next configured token",
                    i + 1, tokens.Count, ex.Failure);

                lastError = ex;
            }
        }

        throw lastError ?? new ProviderException("All configured Apify tokens failed.");
    }

    /// <summary>Kicks off the actor run and returns its run id.</summary>
    private async Task<string> StartRunAsync(
        HttpClient client,
        string actorId,
        string token,
        ProviderQuery query,
        ProviderContext context,
        CancellationToken ct)
    {
        var input = new Dictionary<string, object?>
        {
            ["searchStringsArray"] = new[] { query.CategoryLabel },
            ["locationQuery"] = string.Join(
                ", ",
                new[] { query.Location.City, query.Location.State, query.Location.Country }
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
            ["maxCrawledPlacesPerSearch"] = Math.Clamp(query.MaxResults, 1, 500),
            ["language"] = "en",
            // Detail pages and contact scraping are each separately billed
            // Apify events; the base search result already has everything this
            // app's ProviderBusiness shape uses, and enrichment covers contacts.
            ["scrapePlaceDetailPage"] = false,
            ["scrapeContacts"] = false,
        };

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
            throw new ProviderException("Apify rate limit exceeded for this run.", ProviderFailure.QuotaExceeded, ex);
        }

        using var responseScope = response;

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ProviderException(
                "Apify rejected the API token. Check that it is valid and the actor is accessible to this account.",
                ProviderFailure.InvalidApiKey);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ProviderException(
                $"Apify actor \"{actorId}\" was not found. Check Scraper:ApifyActorId.",
                ProviderFailure.InvalidApiKey);
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
            throw new ProviderException("Apify did not return a run id.");
        }

        return runId;
    }

    /// <summary>Polls run status until it finishes, and returns the resulting dataset id.</summary>
    private async Task<string> PollUntilFinishedAsync(
        HttpClient client,
        string runId,
        string token,
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
                    $"Apify run {runId} did not finish within {context.Options.ApifyRunTimeoutMs / 1000}s.");
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
                        throw new ProviderException($"Apify run {runId} succeeded but returned no dataset.");
                    }

                    return datasetId;

                case "FAILED" or "TIMED-OUT" or "ABORTED":
                    throw new ProviderException($"Apify run {runId} ended with status {status}.");

                default:
                    // READY, RUNNING, TIMING-OUT, ABORTING — still in flight.
                    await Task.Delay(PollInterval, ct);
                    break;
            }
        }
    }

    private async Task<IReadOnlyList<ProviderBusiness>> FetchResultsAsync(
        HttpClient client,
        string datasetId,
        string token,
        ProviderQuery query,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiBase}/datasets/{datasetId}/items?clean=1&limit={query.MaxResults}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderException($"Apify error fetching results: HTTP {(int)response.StatusCode}");
        }

        var places = await response.Content.ReadFromJsonAsync<List<ApifyPlace>>(JsonOptions, ct) ?? [];
        var results = new List<ProviderBusiness>(places.Count);

        foreach (var place in places)
        {
            if (results.Count >= query.MaxResults) break;

            // A closed listing is not a lead, same rule Google Places applies.
            if (place.PermanentlyClosed == true || place.TemporarilyClosed == true) continue;

            results.Add(ToBusiness(place, query));
        }

        return results;
    }

    private static ProviderBusiness ToBusiness(ApifyPlace place, ProviderQuery query)
    {
        var name = (place.Title ?? string.Empty).Trim();
        var address = (place.Address ?? string.Empty).Trim();

        return new ProviderBusiness
        {
            ExternalId = place.PlaceId ?? string.Empty,
            Name = name,
            Category = query.CategoryLabel,
            Address = address,
            Country = Fallback(place.CountryCode, query.Location.Country),
            State = Fallback(place.State, query.Location.State),
            City = Fallback(place.City, query.Location.City),
            Phone = (place.PhoneUnformatted ?? place.Phone ?? string.Empty).Trim(),
            Website = ProviderHelpers.NormalizeUrl(place.Website),
            Latitude = place.Location?.Lat,
            Longitude = place.Location?.Lng,
            Rating = place.TotalScore,
            ReviewCount = place.ReviewsCount,
            MapsUrl = string.IsNullOrWhiteSpace(place.Url)
                ? ProviderHelpers.BuildMapsSearchUrl(name, address)
                : place.Url,
            Source = BusinessSource.Apify,
        };
    }

    private static string Fallback(string? value, string alternative) =>
        string.IsNullOrWhiteSpace(value) ? alternative : value.Trim();

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300];

    // --- wire formats -------------------------------------------------------

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

    private sealed class ApifyPlace
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("categoryName")] public string? CategoryName { get; set; }
        [JsonPropertyName("address")] public string? Address { get; set; }
        [JsonPropertyName("city")] public string? City { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("countryCode")] public string? CountryCode { get; set; }
        [JsonPropertyName("phone")] public string? Phone { get; set; }
        [JsonPropertyName("phoneUnformatted")] public string? PhoneUnformatted { get; set; }
        [JsonPropertyName("website")] public string? Website { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("placeId")] public string? PlaceId { get; set; }
        [JsonPropertyName("location")] public ApifyLatLng? Location { get; set; }
        [JsonPropertyName("totalScore")] public double? TotalScore { get; set; }
        [JsonPropertyName("reviewsCount")] public int? ReviewsCount { get; set; }
        [JsonPropertyName("permanentlyClosed")] public bool? PermanentlyClosed { get; set; }
        [JsonPropertyName("temporarilyClosed")] public bool? TemporarilyClosed { get; set; }
    }

    private sealed class ApifyLatLng
    {
        [JsonPropertyName("lat")] public double? Lat { get; set; }
        [JsonPropertyName("lng")] public double? Lng { get; set; }
    }
}
