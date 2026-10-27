using System.Globalization;
using System.Text.RegularExpressions;

namespace Gov.News.Settings;

/// <summary>A problem that blocks publishing, with the JSON path of the field it belongs to.</summary>
public sealed record ValidationError(string Field, string Message);

/// <summary>
/// The rules every settings document must pass before it can be published.
/// Editor input ends up on every public page, so this is the safety net for both
/// readability (contrast) and injection (no markup, safe link schemes).
/// </summary>
public static partial class SettingsValidator
{
    public const double MinimumContrast = 4.5; // WCAG 2.x AA, normal text

    public static readonly IReadOnlySet<string> BannerStyles = new HashSet<string> { "info", "emergency" };
    public static readonly IReadOnlySet<string> BridgeLogoModes = new HashSet<string> { "auto", "on", "off" };
    public static readonly IReadOnlySet<string> FontFamilies = new HashSet<string> { "BCSans", "Noto Sans", "Verdana" };

    public static IReadOnlyList<ValidationError> Validate(SiteSettings settings)
    {
        var errors = new List<ValidationError>();
        ValidateBanner(settings.Banner, errors);
        ValidateTheme(settings.Theme, errors);
        ValidateLinks("footer.helpLinks", settings.Footer.HelpLinks, errors);
        ValidateLinks("footer.connectLinks", settings.Footer.ConnectLinks, errors);
        ValidateLinks("footer.moreInfoLinks", settings.Footer.MoreInfoLinks, errors);
        if (string.IsNullOrWhiteSpace(settings.Footer.CopyrightHolder))
            errors.Add(new("footer.copyrightHolder", "Enter who holds the copyright."));
        ValidateLinks("connect.facebook", settings.Connect.Facebook, errors);
        ValidateLinks("connect.x", settings.Connect.X, errors);
        ValidateLinks("connect.instagram", settings.Connect.Instagram, errors);
        ValidateLinks("connect.youtube", settings.Connect.Youtube, errors);
        return errors;
    }

    private static void ValidateBanner(BannerSettings banner, List<ValidationError> errors)
    {
        if (!BannerStyles.Contains(banner.Style))
            errors.Add(new("banner.style", "Choose Information or Emergency."));
        if (banner.Enabled && string.IsNullOrWhiteSpace(banner.Message))
            errors.Add(new("banner.message", "Enter a message, or turn the banner off."));
        if (banner.Message.Length > BannerSettings.MaxMessageLength)
            errors.Add(new("banner.message", $"Keep the message to {BannerSettings.MaxMessageLength} characters or fewer."));
        if (banner.Message.Contains('<') || banner.Message.Contains('>'))
            errors.Add(new("banner.message", "Use plain text. Add links below instead of HTML."));
        if (banner.StartsAt is not null && banner.EndsAt is not null && banner.EndsAt <= banner.StartsAt)
            errors.Add(new("banner.endsAt", "The end time must be after the start time."));
        ValidateLinks("banner.links", banner.Links, errors);
    }

    private static void ValidateTheme(ThemeSettings theme, List<ValidationError> errors)
    {
        var c = theme.Colors;
        var colors = new (string Field, string Value)[]
        {
            ("primary", c.Primary), ("text", c.Text), ("link", c.Link), ("bannerInfo", c.BannerInfo),
            ("bannerEmergency", c.BannerEmergency), ("bannerSuccess", c.BannerSuccess), ("warning", c.Warning),
            ("footerBackground", c.FooterBackground), ("border", c.Border),
        };
        var allValid = true;
        foreach (var (field, value) in colors)
        {
            if (!IsHexColor(value))
            {
                errors.Add(new($"theme.colors.{field}", "Use a colour like #013366."));
                allValid = false;
            }
        }

        if (allValid)
        {
            const string white = "#FFFFFF";
            CheckContrast("theme.colors.primary", c.Primary, white, "on white", errors);
            CheckContrast("theme.colors.text", c.Text, white, "on white", errors);
            CheckContrast("theme.colors.link", c.Link, white, "on white", errors);
            CheckContrast("theme.colors.bannerInfo", c.BannerInfo, white, "behind white text", errors);
            CheckContrast("theme.colors.bannerEmergency", c.BannerEmergency, white, "behind white text", errors);
            CheckContrast("theme.colors.bannerSuccess", c.BannerSuccess, white, "behind white text", errors);
            CheckContrast("theme.colors.footerBackground", c.FooterBackground, c.Text, "behind body text", errors);
        }

        if (!FontFamilies.Contains(theme.FontFamily))
            errors.Add(new("theme.fontFamily", "Choose one of the listed typefaces."));
        if (!BridgeLogoModes.Contains(theme.BridgeLogo))
            errors.Add(new("theme.bridgeLogo", "Choose Automatic, Always show or Never show."));
    }

    private static void CheckContrast(string field, string color, string against, string where, List<ValidationError> errors)
    {
        var ratio = ContrastRatio(color, against);
        if (ratio < MinimumContrast)
            errors.Add(new(field, $"Too faint to read {where} ({ratio:0.0}:1, needs {MinimumContrast}:1). Pick a darker shade."));
    }

    private static void ValidateLinks(string field, List<LinkSetting> links, List<ValidationError> errors)
    {
        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            if (string.IsNullOrWhiteSpace(link.Text))
                errors.Add(new($"{field}[{i}].text", "Enter the link text."));
            if (!IsSafeUrl(link.Url))
                errors.Add(new($"{field}[{i}].url", "Must start with https:// or / (a page on this site)."));
        }
    }

    /// <summary>An absolute https URL, or a path on this site. Rejects http:, javascript:, data: and //host.</summary>
    public static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url != url.Trim())
            return false;
        if (url.StartsWith('/'))
            return !url.StartsWith("//") && !url.StartsWith("/\\");
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0;
    }

    public static bool IsHexColor(string? value) => value is not null && HexColor().IsMatch(value);

    /// <summary>WCAG 2.x contrast ratio between two #RRGGBB colours.</summary>
    public static double ContrastRatio(string a, string b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        double Channel(int offset)
        {
            var v = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
