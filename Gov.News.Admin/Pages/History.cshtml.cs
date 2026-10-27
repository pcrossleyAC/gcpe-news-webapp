using System.Security.Claims;
using Gov.News.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gov.News.Admin.Pages;

public class HistoryModel(BlobSiteSettingsStore store) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }

    public StoredSettings? Live { get; private set; }
    public IReadOnlyList<HistoryEntry> Entries { get; private set; } = [];

    /// <summary>Set when the editor has asked to restore a revision and is being shown what it would change.</summary>
    public SiteSettings? Candidate { get; private set; }
    public IReadOnlyList<string> CandidateChanges { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? restore, CancellationToken ct)
    {
        await LoadAsync(ct);
        if (restore is not null)
        {
            var old = await store.GetRevisionAsync(restore.Value, ct);
            if (old is null)
                return NotFound();
            Candidate = old;
            CandidateChanges = SiteSettings.DifferingSections(old, Live?.Settings ?? SiteSettings.Defaults());
        }
        return Page();
    }

    public async Task<IActionResult> OnPostRestoreAsync(int revision, string? baseETag, CancellationToken ct)
    {
        var result = await store.RestoreAsync(revision, string.IsNullOrEmpty(baseETag) ? null : baseETag, CurrentUser(), ct);
        StatusMessage = result.Status switch
        {
            PublishStatus.Published => $"Revision {revision} is live again, as revision {result.Live!.Settings.Revision}. Visitors see it within two minutes.",
            PublishStatus.Conflict => $"Nothing was restored: revision {result.Live?.Settings.Revision} was published while you were looking. Check it and try again.",
            PublishStatus.NotFound => $"Revision {revision} doesn't exist.",
            _ => $"Revision {revision} can't be published as it is: " + string.Join(" ", result.Errors.Select(e => e.Message)),
        };
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Live = await store.GetLiveAsync(ct);
        Entries = await store.ListHistoryAsync(ct);
    }

    private string CurrentUser() =>
        User.FindFirstValue("preferred_username") ?? User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";
}
