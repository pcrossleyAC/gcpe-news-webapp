using System.Reflection;
using System.Text.Json;

namespace Gov.News.Settings;

/// <summary>
/// Everything a non-developer can change about the site, stored as one JSON document.
/// Property names serialize as camelCase to match <c>defaults.json</c>.
/// </summary>
public sealed class SiteSettings
{
    public int SchemaVersion { get; set; } = 1;
    public int Revision { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? PublishedBy { get; set; }
    public string? Note { get; set; }

    public BannerSettings Banner { get; set; } = new();
    public ThemeSettings Theme { get; set; } = new();
    public FooterSettings Footer { get; set; } = new();
    public ConnectSettings Connect { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static SiteSettings FromJson(string json) =>
        JsonSerializer.Deserialize<SiteSettings>(json, JsonOptions)
        ?? throw new JsonException("Settings document is empty.");

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Names of the sections whose content differs between two documents, ignoring revision metadata.</summary>
    public static IReadOnlyList<string> DifferingSections(SiteSettings a, SiteSettings b)
    {
        static string Json(object section) => JsonSerializer.Serialize(section, JsonOptions);
        var sections = new (string Name, object A, object B)[]
        {
            ("Banner", a.Banner, b.Banner),
            ("Theme", a.Theme, b.Theme),
            ("Footer", a.Footer, b.Footer),
            ("Social links", a.Connect, b.Connect),
        };
        return sections.Where(s => Json(s.A) != Json(s.B)).Select(s => s.Name).ToList();
    }

    /// <summary>The values the site shipped with, used when no settings have been published.</summary>
    public static SiteSettings Defaults()
    {
        using var stream = typeof(SiteSettings).Assembly.GetManifestResourceStream("Gov.News.Settings.defaults.json")
            ?? throw new InvalidOperationException("defaults.json is not embedded in Gov.News.Settings.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }
}

public sealed class BannerSettings
{
    public const int MaxMessageLength = 300;

    public bool Enabled { get; set; }
    /// <summary><c>info</c> (blue) or <c>emergency</c> (red).</summary>
    public string Style { get; set; } = "info";
    /// <summary>Plain text only; links go in <see cref="Links"/>.</summary>
    public string Message { get; set; } = "";
    public List<LinkSetting> Links { get; set; } = [];
    public bool Dismissible { get; set; } = true;
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }

    public bool IsVisibleAt(DateTimeOffset now) =>
        Enabled && (StartsAt is null || now >= StartsAt) && (EndsAt is null || now < EndsAt);
}

public sealed class ThemeSettings
{
    public ThemeColors Colors { get; set; } = new();
    public string FontFamily { get; set; } = "BCSans";
    /// <summary><c>auto</c> (follow the live-event signal), <c>on</c> or <c>off</c>.</summary>
    public string BridgeLogo { get; set; } = "auto";
}

public sealed class ThemeColors
{
    public string Primary { get; set; } = "#013366";
    public string Text { get; set; } = "#2D2D2D";
    public string Link { get; set; } = "#255A90";
    public string BannerInfo { get; set; } = "#013366";
    public string BannerEmergency { get; set; } = "#CE3E39";
    public string BannerSuccess { get; set; } = "#42814A";
    public string Warning { get; set; } = "#F8BB47";
    public string FooterBackground { get; set; } = "#F2F2F2";
    public string Border { get; set; } = "#E2E2E2";
}

public sealed class FooterSettings
{
    public List<LinkSetting> HelpLinks { get; set; } = [];
    public List<LinkSetting> ConnectLinks { get; set; } = [];
    public List<LinkSetting> MoreInfoLinks { get; set; } = [];
    /// <summary>The year is added when the page renders.</summary>
    public string CopyrightHolder { get; set; } = "";
}

public sealed class ConnectSettings
{
    public List<LinkSetting> Facebook { get; set; } = [];
    public List<LinkSetting> X { get; set; } = [];
    public List<LinkSetting> Instagram { get; set; } = [];
    public List<LinkSetting> Youtube { get; set; } = [];
}

public sealed class LinkSetting
{
    public string Text { get; set; } = "";
    public string Url { get; set; } = "";
}
