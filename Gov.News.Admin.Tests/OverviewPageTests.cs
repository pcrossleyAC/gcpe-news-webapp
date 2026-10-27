using System.Net;
using Gov.News.Settings;

namespace Gov.News.Admin.Tests;

public class OverviewPageTests
{
    [AzuriteFact]
    public async Task Publish_times_are_shown_in_Pacific_time()
    {
        using var app = new AdminApp();
        var live = (await app.Store.PublishAsync(SiteSettings.Defaults(), null, "jane", "First")).Live!;
        var editor = app.SignIn("jane");

        var response = await editor.Client.GetAsync("/");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        var pacific = PacificTime.Format(live.Settings.PublishedAt);
        var utc = live.Settings.PublishedAt!.Value.UtcDateTime.ToString("MMM d, yyyy h:mm tt");
        Assert.NotEqual(utc, pacific);
        Assert.Contains(pacific, html);
        Assert.DoesNotContain(utc, html);
    }
}
