using Gov.News.Settings;
using Microsoft.AspNetCore.Mvc;

namespace Gov.News.Admin.Pages;

public class BannerModel(BlobSiteSettingsStore store) : SettingsEditorModel(store)
{
    [BindProperty] public BannerInput Input { get; set; } = new();

    public override string SectionName => "Banner";
    protected override string ErrorPrefix => "banner.";

    protected override void LoadInput(SiteSettings source) => Input = BannerInput.From(source.Banner);

    protected override void ApplyInput(SiteSettings document) => document.Banner = Input.ToBanner(document.Banner.Style);

    protected override List<LinkSetting>? LinkList(string key) => key == "Links" ? Input.Links : null;
}

/// <summary>The banner as the form shows it: one Off/Information/Emergency choice, times in Pacific time.</summary>
public sealed class BannerInput
{
    public string Mode { get; set; } = "off";
    public string Message { get; set; } = "";
    public List<LinkSetting> Links { get; set; } = [];
    public bool Dismissible { get; set; } = true;
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    public static BannerInput From(BannerSettings banner) => new()
    {
        Mode = banner.Enabled ? banner.Style : "off",
        Message = banner.Message,
        Links = banner.Links.Select(l => l.Copy()).ToList(),
        Dismissible = banner.Dismissible,
        StartsAt = PacificTime.ToLocal(banner.StartsAt),
        EndsAt = PacificTime.ToLocal(banner.EndsAt),
    };

    /// <param name="previousStyle">Kept when the banner is turned off, so turning it back on restores it.</param>
    public BannerSettings ToBanner(string previousStyle) => new()
    {
        Enabled = Mode != "off",
        Style = Mode == "off" ? previousStyle : Mode,
        Message = (Message ?? "").Trim(),
        Links = Links
            .Where(l => !string.IsNullOrWhiteSpace(l.Text) || !string.IsNullOrWhiteSpace(l.Url))
            .Select(l => new LinkSetting { Text = (l.Text ?? "").Trim(), Url = (l.Url ?? "").Trim() })
            .ToList(),
        Dismissible = Dismissible,
        StartsAt = PacificTime.FromLocal(StartsAt),
        EndsAt = PacificTime.FromLocal(EndsAt),
    };
}
