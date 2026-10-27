using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gov.News.Settings;

/// <summary>
/// Everything a non-developer can change about the site, stored as one JSON document.
/// Property names serialize as camelCase to match <c>defaults.json</c>.
/// </summary>
public sealed class SiteSettings
{
    /// <summary>
    /// 2: social links became per-network featured + accounts lists, and the theme was wired to the
    /// public stylesheets. Neither had any effect in version 1, so upgrading takes the defaults for both.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public int Revision { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? PublishedBy { get; set; }
    public string? Note { get; set; }

    public BannerSettings Banner { get; set; } = new();
    public ThemeSettings Theme { get; set; } = new();
    public FooterSettings Footer { get; set; } = new();
    public ConnectSettings Connect { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static SiteSettings FromJson(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Settings document is empty.");
        var version = node["schemaVersion"]?.GetValue<int>() ?? 1;
        if (version < 2)
            node.Remove("connect"); // version 1 social lists and theme were never used; take the defaults below
        var settings = node.Deserialize<SiteSettings>(JsonOptions) ?? throw new JsonException("Settings document is empty.");
        if (version < 2)
        {
            var defaults = DefaultsDocument();
            settings.Connect = defaults.Connect;
            settings.Theme = defaults.Theme;
            settings.SchemaVersion = CurrentSchemaVersion;
        }
        return settings;
    }

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
    public static SiteSettings Defaults() => DefaultsDocument();

    private static SiteSettings DefaultsDocument()
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

/// <summary>
/// Colours the public stylesheets read as CSS variables (see _Layout). Defaults are the site's
/// existing colours, so an unchanged theme renders exactly as before.
/// </summary>
public sealed class ThemeColors
{
    /// <summary>--primary-blue: buttons, rules and the default banner.</summary>
    public string Primary { get; set; } = "#013366";
    /// <summary>--theme-text: body text (#2D2D2D throughout the stylesheets).</summary>
    public string Text { get; set; } = "#2D2D2D";
    /// <summary>--theme-link: links, e.g. the ministry name above headlines (#003366 throughout the stylesheets).</summary>
    public string Link { get; set; } = "#003366";
    public string BannerInfo { get; set; } = "#013366";
    public string BannerEmergency { get; set; } = "#D8292F";
    /// <summary>--theme-footer-background.</summary>
    public string FooterBackground { get; set; } = "#FFFFFF";
}

public sealed class FooterSettings
{
    /// <summary>Sentence before the help links, e.g. "We can help in over 220 languages…".</summary>
    public string HelpText { get; set; } = "";
    public List<LinkSetting> HelpLinks { get; set; } = [];
    public List<LinkSetting> ConnectLinks { get; set; } = [];
    public List<LinkSetting> MoreInfoLinks { get; set; } = [];
    /// <summary>The year is added when the page renders.</summary>
    public string CopyrightHolder { get; set; } = "";
}

/// <summary>The accounts listed on the public Connect page, network by network. RSS feeds are generated, not listed here.</summary>
public sealed class ConnectSettings
{
    public SocialNetworkLinks Facebook { get; set; } = new();
    public SocialNetworkLinks X { get; set; } = new();
    public SocialNetworkLinks Instagram { get; set; } = new();
    public SocialNetworkLinks Threads { get; set; } = new();
    public SocialNetworkLinks Bluesky { get; set; } = new();
    public SocialNetworkLinks Flickr { get; set; } = new();
    public SocialNetworkLinks Youtube { get; set; } = new();
    public SocialNetworkLinks Blogs { get; set; } = new();

    /// <summary>Every network with its JSON key and display name, in Connect page order.</summary>
    public IEnumerable<(string Key, string Name, SocialNetworkLinks Links)> Networks()
    {
        yield return ("facebook", "Facebook", Facebook);
        yield return ("x", "X", X);
        yield return ("instagram", "Instagram", Instagram);
        yield return ("threads", "Threads", Threads);
        yield return ("bluesky", "Bluesky", Bluesky);
        yield return ("flickr", "Flickr", Flickr);
        yield return ("youtube", "YouTube", Youtube);
        yield return ("blogs", "Blogs", Blogs);
    }
}

public sealed class SocialNetworkLinks
{
    /// <summary>Shown first, in this order (e.g. the main government account).</summary>
    public List<LinkSetting> Featured { get; set; } = [];
    /// <summary>Shown after the featured accounts, sorted by name.</summary>
    public List<LinkSetting> Accounts { get; set; } = [];

    public IEnumerable<LinkSetting> InDisplayOrder() => Featured.Concat(Accounts.OrderBy(a => a.Text));
}

public sealed class LinkSetting
{
    public string Text { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>
    /// Optional HTML id kept from the original markup (functional tests and analytics use them).
    /// Not editable in the admin; carried through unchanged. Omitted from JSON when null.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    /// <summary>Optional one-line description, shown next to social accounts.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; set; }

    public LinkSetting Copy() => new() { Text = Text, Url = Url, Id = Id, Summary = Summary };
}
