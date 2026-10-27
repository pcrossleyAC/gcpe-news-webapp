namespace Gov.News.Settings;

/// <summary>Where the public site gets its current settings from. Implementations never throw.</summary>
public interface ISiteSettingsSource
{
    Task<SiteSettings> GetAsync(CancellationToken ct = default);
}

/// <summary>Used when no settings storage is configured: the site behaves exactly as its built-in defaults.</summary>
public sealed class DefaultSiteSettingsSource : ISiteSettingsSource
{
    private readonly SiteSettings _defaults = SiteSettings.Defaults();

    public Task<SiteSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(_defaults);
}
