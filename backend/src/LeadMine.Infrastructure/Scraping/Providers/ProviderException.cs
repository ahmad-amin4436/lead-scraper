namespace LeadMine.Infrastructure.Scraping.Providers;

/// <summary>Why a provider could not return results.</summary>
public enum ProviderFailure
{
    /// <summary>Network, quota or an unexpected response — worth retrying later.</summary>
    Transient = 0,

    /// <summary>No key configured. The run cannot proceed on this provider.</summary>
    MissingApiKey = 1,

    /// <summary>A key is configured but the provider rejected it.</summary>
    InvalidApiKey = 2,

    /// <summary>The provider has no mapping for the requested category.</summary>
    UnsupportedCategory = 3,

    /// <summary>
    /// The provider's quota is exhausted for the current window (HTTP 429).
    /// <para>
    /// Not <see cref="IsFatal"/>: a billed provider running dry does not mean
    /// the free, unmetered ones are also out of road. The runner treats this as
    /// a signal to switch the rest of the sweep onto OpenStreetMap rather than
    /// aborting or retrying against a wall that has not had time to reset.
    /// </para>
    /// </summary>
    QuotaExceeded = 4,
}

/// <summary>
/// A provider-level failure carrying enough classification for the runner to
/// decide whether to abandon the whole run or just this task.
/// <para>
/// A bad API key fails every remaining task identically, so the run stops. A
/// timeout on one city does not, so the run continues.
/// </para>
/// </summary>
public sealed class ProviderException(string message, ProviderFailure failure = ProviderFailure.Transient, Exception? inner = null)
    : Exception(message, inner)
{
    public ProviderFailure Failure { get; } = failure;

    /// <summary>True when retrying other tasks is pointless.</summary>
    public bool IsFatal => Failure is ProviderFailure.MissingApiKey or ProviderFailure.InvalidApiKey;
}
