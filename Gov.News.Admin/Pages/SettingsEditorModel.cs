using System.Security.Claims;
using Gov.News.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gov.News.Admin.Pages;

/// <summary>
/// The draft / publish / conflict flow shared by every settings screen. A screen edits one
/// section of the settings document; it says how to fill its form from a document
/// (<see cref="LoadInput"/>) and how to write the form back (<see cref="ApplyInput"/>).
/// </summary>
public abstract class SettingsEditorModel(BlobSiteSettingsStore store) : PageModel
{
    protected BlobSiteSettingsStore Store => store;

    /// <summary>ETag of the live settings this edit builds on; round-trips through the form.</summary>
    [BindProperty] public string? BaseETag { get; set; }
    [BindProperty] public string? Note { get; set; }

    [TempData] public string? StatusMessage { get; set; }

    public StoredSettings? Live { get; private set; }
    public bool HasDraft { get; private set; }
    /// <summary>The document the page is showing: the editor's draft, else what is live, else the defaults.</summary>
    public SiteSettings Current { get; private set; } = SiteSettings.Defaults();
    public IReadOnlyList<ValidationError> Errors { get; protected set; } = [];
    /// <summary>Set when someone else published first: what is live now, and which sections differ from this draft.</summary>
    public SiteSettings? ConflictLive { get; private set; }
    public IReadOnlyList<string> ConflictSections { get; private set; } = [];

    /// <summary>The section this screen edits, as <see cref="SiteSettings.DifferingSections"/> names it.</summary>
    public abstract string SectionName { get; }
    /// <summary>Validation field prefix for this section, e.g. "banner.".</summary>
    protected abstract string ErrorPrefix { get; }
    protected abstract void LoadInput(SiteSettings source);
    protected abstract void ApplyInput(SiteSettings document);
    /// <summary>The editable link list called <paramref name="key"/> in the form, if this screen has one.</summary>
    protected virtual List<LinkSetting>? LinkList(string key) => null;

    public async Task OnGetAsync(CancellationToken ct)
    {
        var draft = await LoadContextAsync(ct);
        LoadInput(Current);
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
                StatusMessage = $"Published revision {result.Live!.Settings.Revision}. Visitors see it within two minutes.";
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
    /// After a conflict: start again from what is live now, keeping only this screen's section.
    /// Other unpublished edits in the draft are set aside, and the page says which.
    /// </summary>
    public async Task<IActionResult> OnPostRebaseAsync(CancellationToken ct)
    {
        var oldDraft = (await store.GetDraftAsync(CurrentUser(), ct))?.Settings;
        var live = await store.GetLiveAsync(ct);
        var document = SiteSettings.FromJson((live?.Settings ?? SiteSettings.Defaults()).ToJson());
        ApplyInput(document);
        BaseETag = live?.ETag;
        await store.SaveDraftAsync(CurrentUser(), document, BaseETag, ct);

        var dropped = oldDraft is null || live is null
            ? []
            : SiteSettings.DifferingSections(oldDraft, live.Settings).Where(s => s != SectionName).ToList();
        StatusMessage = $"Your {SectionName.ToLowerInvariant()} changes now build on revision {live?.Settings.Revision ?? 0}. Check them, then publish."
            + (dropped.Count > 0 ? $" Unpublished changes to {string.Join(", ", dropped)} were set aside." : "");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDiscardAsync(CancellationToken ct)
    {
        await store.DiscardDraftAsync(CurrentUser(), ct);
        StatusMessage = "Draft discarded. Showing what is live.";
        return RedirectToPage();
    }

    public Task<IActionResult> OnPostAddLinkAsync(string list, CancellationToken ct) =>
        EditLinksAsync(list, links => links.Add(new LinkSetting()), ct);

    public Task<IActionResult> OnPostRemoveLinkAsync(string list, int index, CancellationToken ct) =>
        EditLinksAsync(list, links =>
        {
            if (index >= 0 && index < links.Count)
                links.RemoveAt(index);
        }, ct);

    /// <param name="by">-1 moves the link up one place, +1 down one place.</param>
    public Task<IActionResult> OnPostMoveLinkAsync(string list, int index, int by, CancellationToken ct) =>
        EditLinksAsync(list, links =>
        {
            var target = index + Math.Sign(by);
            if (index >= 0 && index < links.Count && target >= 0 && target < links.Count)
                (links[index], links[target]) = (links[target], links[index]);
        }, ct);

    public IEnumerable<string> ErrorsFor(string field) =>
        Errors.Where(e => e.Field == field).Select(e => e.Message);

    public IEnumerable<ValidationError> OtherSectionErrors =>
        Errors.Where(e => !e.Field.StartsWith(ErrorPrefix, StringComparison.Ordinal));

    protected string CurrentUser() =>
        User.FindFirstValue("preferred_username") ?? User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";

    private async Task<IActionResult> EditLinksAsync(string list, Action<List<LinkSetting>> edit, CancellationToken ct)
    {
        var links = LinkList(list);
        if (links is null)
            return BadRequest();
        edit(links);
        ModelState.Clear();
        await LoadContextAsync(ct);
        return Page();
    }

    /// <summary>The editor's whole settings document with this screen's form applied.</summary>
    private async Task<SiteSettings> BuildDocumentAsync(CancellationToken ct)
    {
        var draft = await store.GetDraftAsync(CurrentUser(), ct);
        var live = draft is null ? await store.GetLiveAsync(ct) : null;
        var document = SiteSettings.FromJson((draft?.Settings ?? live?.Settings ?? SiteSettings.Defaults()).ToJson());
        ApplyInput(document);

        // Show exactly what was saved (e.g. blank link rows dropped), so error positions line up with the rows on screen.
        LoadInput(document);
        ModelState.Clear();
        return document;
    }

    private async Task<DraftSettings?> LoadContextAsync(CancellationToken ct)
    {
        Live = await store.GetLiveAsync(ct);
        var draft = await store.GetDraftAsync(CurrentUser(), ct);
        HasDraft = draft is not null;
        Current = draft?.Settings ?? Live?.Settings ?? SiteSettings.Defaults();
        return draft;
    }

    /// <summary>Drops rows with neither text nor address, trims the rest, and keeps any fixed id.</summary>
    protected static List<LinkSetting> CleanLinks(IEnumerable<LinkSetting> links) => links
        .Where(l => !string.IsNullOrWhiteSpace(l.Text) || !string.IsNullOrWhiteSpace(l.Url))
        .Select(l => new LinkSetting { Text = (l.Text ?? "").Trim(), Url = (l.Url ?? "").Trim(), Id = string.IsNullOrWhiteSpace(l.Id) ? null : l.Id })
        .ToList();
}
