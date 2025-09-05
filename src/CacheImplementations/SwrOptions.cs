namespace CacheImplementations;

/// <summary>
/// Options controlling Stale-While-Revalidate behavior.
/// </summary>
/// <param name="Ttl">Duration the value is considered fresh.</param>
/// <param name="Stale">Additional duration the value may be served stale while a background refresh occurs.</param>
public sealed record SwrOptions(TimeSpan Ttl, TimeSpan Stale);
