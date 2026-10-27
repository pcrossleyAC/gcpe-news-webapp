using System.Security.Claims;
using Gov.News.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gov.News.Admin.Pages;

public class BannerModel(BlobSiteSettingsStore store) : PageModel
{
    [BindProperty] public BannerInput Input { get; set; } = new();
    /// <summary>ETag of the live settings this edit builds on; round-trips through the form.</summary>
    [BindProperty] public string? BaseETag { get; set; }
    [BindProperty] public string? Note { get; set; }

    [TempData] public string? StatusMessage { get; set; }

    public StoredSettings? Live { get; private set; }
    public bool HasDraft { get; private set; }
    public ThemeColors Colors { get; private set; } = new();
    public IReadOnlyList<ValidationError> Errors { get; private set; } = [];
    /// <summary>Set when someone else published first: what is live now, and which sections differ from this draft.</summary>
    public SiteSettings? ConflictLive { get; private set; }
    public IReadOnlyList<string> ConflictSections { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var draft = await LoadContextAsync(ct);
        var source = draft?.Settings ?? Live?.Settings ?? SiteSettings.Defaults();
        Input = BannerInput.From(source.Banner);
        BaseETag = draft is not null ? draft.BaseLiveETag : Live?.ETag;
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        var document = await BuildDocumentAsync(ct);
        await store.SaveDraftAsync(CurrentUser(), document, BaseETag, ct);
        Errors = SettingsValidator.Validate(document);
        if (Errors.Count == 0)
        {
            StatusMessage = "Draft saved. It isn't live until you publish.";
            return RedirectToPage();
        }
        StatusMessage = "Draft saved, but it has problems to fix before it can be published.";
        await LoadContextAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostPublishAsync(CancellationToken ct)
    {
        var document = await BuildDocumentAsync(ct);
        await store.SaveDraftAsync(CurrentUser(), document, BaseETag, ct);
        var result = await store.PublishAsync(document, BaseETag, CurrentUser(), Note, ct);
        switch (result.Status)
        {
            case PublishStatus.Published:
                StatusMessage = $"Published revision {result.Live!.Settings.Revision}. The public site picks it up within a minute.";
                return RedirectToPage();
            case PublishStatus.Conflict:
                ConflictLive = result.Live?.Settings;
                ConflictSections = ConflictLive is null ? [] : SiteSettings.DifferingSections(document, ConflictLive);
                break;
            default:
                Errors = result.Errors;
                break;
        }
        await LoadContextAsync(ct);
        return Page();
    }

    /// <summary>
    /// After a conflict: start again from what is live now, keeping only this banner.
    /// Other unpublished edits in the draft are dropped, and the page says which.
    /// </summary>
    public async Task<IActionResult> OnPostRebaseAsync(CancellationToken ct)
    {
        var oldDraft = (await store.GetDraftAsync(CurrentUser(), ct))?.Settings;
        var live = await store.GetLiveAsync(ct);
        var document = SiteSettings.FromJson((live?.Settings ?? SiteSettings.Defaults()).ToJson());
        document.Banner = Input.ToBanner(document.Banner.Style);
        BaseETag = live?.ETag;
        await store.SaveDraftAsync(CurrentUser(), document, BaseETag, ct);

        var dropped = oldDraft is null || live is null
            ? []
            : SiteSettings.DifferingSections(oldDraft, live.Settings).Where(s => s != "Banner").ToList();
        StatusMessage = $"Your banner now builds on revision {live?.Settings.Revision ?? 0}. Check it, then publish."
            + (dropped.Count > 0 ? $" Unpublished changes to {string.Join(", ", dropped)} were set aside." : "");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDiscardAsync(CancellationToken ct)
    {
        await store.DiscardDraftAsync(CurrentUser(), ct);
        StatusMessage = "Draft discarded. Showing what is live.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddLinkAsync(CancellationToken ct)
    {
        Input.Links.Add(new LinkSetting());
        ModelState.Clear();
        await LoadContextAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveLinkAsync(int index, CancellationToken ct)
    {
        if (index >= 0 && index < Input.Links.Count)
            Input.Links.RemoveAt(index);
        ModelState.Clear();
        await LoadContextAsync(ct);
        return Page();
    }

    public IEnumerable<string> ErrorsFor(string field) =>
        Errors.Where(e => e.Field == field).Select(e => e.Message);

    public IEnumerable<ValidationError> OtherSectionErrors =>
        Errors.Where(e => !e.Field.StartsWith("banner.", StringComparison.Ordinal));

    /// <summary>The whole settings document with this page's banner in place.</summary>
    private async Task<SiteSettings> BuildDocumentAsync(CancellationToken ct)
    {
        var draft = await store.GetDraftAsync(CurrentUser(), ct);
        var live = draft is null ? await store.GetLiveAsync(ct) : null;
        var document = SiteSettings.FromJson((draft?.Settings ?? live?.Settings ?? SiteSettings.Defaults()).ToJson());
        document.Banner = Input.ToBanner(document.Banner.Style);

        // Show exactly what was saved (blank link rows dropped), so error positions line up with the rows on screen.
        Input.Links = document.Banner.Links.Select(l => new LinkSetting { Text = l.Text, Url = l.Url }).ToList();
        ModelState.Clear();
        return document;
    }

    private async Task<DraftSettings?> LoadContextAsync(CancellationToken ct)
    {
        Live = await store.GetLiveAsync(ct);
        var draft = await store.GetDraftAsync(CurrentUser(), ct);
        HasDraft = draft is not null;
        Colors = (draft?.Settings ?? Live?.Settings ?? SiteSettings.Defaults()).Theme.Colors;
        return draft;
    }

    private string CurrentUser() =>
        User.FindFirstValue("preferred_username") ?? User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";
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
        Links = banner.Links.Select(l => new LinkSetting { Text = l.Text, Url = l.Url }).ToList(),
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
