using Gov.News.Settings;
using Microsoft.AspNetCore.Mvc;

namespace Gov.News.Admin.Pages;

public class ThemeModel(BlobSiteSettingsStore store) : SettingsEditorModel(store)
{
    [BindProperty] public ThemeColors Input { get; set; } = new();

    public override string SectionName => "Theme";
    protected override string ErrorPrefix => "theme.";

    /// <summary>Each colour the form offers: field name, label, what it is checked against, and that colour.</summary>
    public IEnumerable<(string Field, string Label, string Hint, string Against)> Colours()
    {
        const string white = "#FFFFFF";
        yield return (nameof(ThemeColors.Primary), "Primary", "Buttons, rules and accents. Checked against white.", white);
        yield return (nameof(ThemeColors.Link), "Links", "Including the ministry name above headlines. Checked against white.", white);
        yield return (nameof(ThemeColors.Text), "Body text", "Checked against white.", white);
        yield return (nameof(ThemeColors.BannerInfo), "Information banner", "White text sits on it.", white);
        yield return (nameof(ThemeColors.BannerEmergency), "Emergency banner", "White text sits on it.", white);
        yield return (nameof(ThemeColors.FooterBackground), "Footer background", "Checked against the body text colour.", Input.Text);
    }

    public string ValueOf(string field) => (string)typeof(ThemeColors).GetProperty(field)!.GetValue(Input)!;

    protected override void LoadInput(SiteSettings source)
    {
        var c = source.Theme.Colors;
        Input = new ThemeColors
        {
            Primary = c.Primary, Link = c.Link, Text = c.Text,
            BannerInfo = c.BannerInfo, BannerEmergency = c.BannerEmergency, FooterBackground = c.FooterBackground,
        };
    }

    protected override void ApplyInput(SiteSettings document) => document.Theme.Colors = new ThemeColors
    {
        Primary = Normalize(Input.Primary),
        Link = Normalize(Input.Link),
        Text = Normalize(Input.Text),
        BannerInfo = Normalize(Input.BannerInfo),
        BannerEmergency = Normalize(Input.BannerEmergency),
        FooterBackground = Normalize(Input.FooterBackground),
    };

    public async Task<IActionResult> OnPostResetAsync(CancellationToken ct)
    {
        Input = SiteSettings.Defaults().Theme.Colors;
        ModelState.Clear();
        StatusMessage = "Colours reset to the BC Gov defaults. Save or publish to keep them.";
        await OnGetContextOnlyAsync(ct);
        return Page();
    }

    private async Task OnGetContextOnlyAsync(CancellationToken ct)
    {
        var keep = Input;
        await OnGetAsync(ct);
        Input = keep;
    }

    // <input type="color"> sends lower case; settings and the validator use #RRGGBB upper case.
    private static string Normalize(string? value) => (value ?? "").Trim().ToUpperInvariant();
}
