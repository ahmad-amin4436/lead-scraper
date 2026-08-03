using System.Text.Json.Serialization;
using LeadMine.Domain.Enums;

namespace LeadMine.Application.DTOs;

/// <summary>
/// The search parameters, as stored on the job and read by the worker.
/// <para>
/// Persisted as JSON on <c>SearchJob.RequestJson</c> so a run is fully described
/// by its row: the worker needs nothing but the job id to execute or resume it.
/// Property names are camelCase to match what the front end sends.
/// </para>
/// </summary>
public sealed class SearchRequestPayload
{
    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = new();

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("cities")]
    public List<string> Cities { get; set; } = new();

    [JsonPropertyName("radiusMeters")]
    public int RadiusMeters { get; set; } = 10000;

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 60;

    [JsonPropertyName("enrichContacts")]
    public bool EnrichContacts { get; set; } = true;

    [JsonPropertyName("skipDuplicates")]
    public bool SkipDuplicates { get; set; } = true;

    /// <summary>Which quality of lead to keep. Applied while the run is live.</summary>
    [JsonPropertyName("leadKind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LeadKind LeadKind { get; set; } = LeadKind.Any;

    [JsonPropertyName("minRating")]
    public double? MinRating { get; set; }

    [JsonPropertyName("minReviews")]
    public int? MinReviews { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>Every (city × category) pair this run will sweep.</summary>
    public IEnumerable<(string City, string Category, string Key)> EnumerateTasks()
    {
        foreach (var city in Cities)
        {
            foreach (var category in Categories)
            {
                yield return (city, category, $"{city}|{category}");
            }
        }
    }

    public int TotalTasks => Cities.Count * Categories.Count;
}

/// <summary>A business as returned by a provider, before it becomes a lead.</summary>
public sealed class ProviderBusiness
{
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Rating { get; set; }
    public int? ReviewCount { get; set; }
    public string MapsUrl { get; set; } = string.Empty;
    public BusinessSource Source { get; set; }
}

/// <summary>A city resolved to coordinates.</summary>
public sealed record ResolvedLocation(
    string Label,
    string Country,
    string State,
    string City,
    double Latitude,
    double Longitude);
