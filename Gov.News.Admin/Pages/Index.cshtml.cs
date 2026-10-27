using Gov.News.Settings;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Gov.News.Admin.Pages;

public class IndexModel(BlobSiteSettingsStore store) : PageModel
{
    public StoredSettings? Live { get; private set; }
    public IReadOnlyList<HistoryEntry> History { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Live = await store.GetLiveAsync(ct);
        History = await store.ListHistoryAsync(ct);
    }
}
