using System.Net;
using System.Text.RegularExpressions;
using Gov.News.Settings;

namespace Gov.News.Admin.Tests;

public class HistoryPageTests
{
    private static SiteSettings WithBanner(string message)
    {
        var settings = SiteSettings.Defaults();
        settings.Banner.Message = message;
        return settings;
    }

    private static string Token(string html) =>
        WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);

    private static string BaseETag(string html) =>
        WebUtility.HtmlDecode(Regex.Match(html, "name=\"baseETag\" value=\"([^\"]*)\"").Groups[1].Value);

    [AzuriteFact]
    public async Task Restore_preview_says_what_would_change_then_restores_as_a_new_revision()
    {
        using var app = new AdminApp();
        var r1 = (await app.Store.PublishAsync(WithBanner("Election period"), null, "sam", "Election")).Live!;
        await app.Store.PublishAsync(WithBanner("Wildfire emergency"), r1.ETag, "sam", "Wildfire");
        var jane = app.SignIn("jane");

        var preview = await (await jane.Client.GetAsync("/History?restore=1")).Content.ReadAsStringAsync();
        Assert.Contains("Restore revision 1?", preview);
        Assert.Contains("this changes: <strong>Banner</strong>", preview);
        Assert.Contains("Election period", preview);

        var response = await jane.Client.PostAsync("/History?handler=Restore", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(preview),
            ["revision"] = "1",
            ["baseETag"] = BaseETag(preview),
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var live = (await app.Store.GetLiveAsync())!.Settings;
        Assert.Equal(3, live.Revision);
        Assert.Equal("Election period", live.Banner.Message);
        Assert.Equal("jane", live.PublishedBy);
        var page = WebUtility.HtmlDecode(await (await jane.Client.GetAsync("/History")).Content.ReadAsStringAsync());
        Assert.Contains("Revision 1 is live again, as revision 3", page);
    }

    [AzuriteFact]
    public async Task Restore_from_a_stale_page_does_nothing()
    {
        using var app = new AdminApp();
        var r1 = (await app.Store.PublishAsync(WithBanner("One"), null, "sam", null)).Live!;
        var r2 = (await app.Store.PublishAsync(WithBanner("Two"), r1.ETag, "sam", null)).Live!;
        var jane = app.SignIn("jane");
        var preview = await (await jane.Client.GetAsync("/History?restore=1")).Content.ReadAsStringAsync();

        await app.Store.PublishAsync(WithBanner("Three"), r2.ETag, "sam", null); // someone publishes meanwhile

        await jane.Client.PostAsync("/History?handler=Restore", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(preview),
            ["revision"] = "1",
            ["baseETag"] = BaseETag(preview),
        }));

        Assert.Equal("Three", (await app.Store.GetLiveAsync())!.Settings.Banner.Message);
        var page = WebUtility.HtmlDecode(await (await jane.Client.GetAsync("/History")).Content.ReadAsStringAsync());
        Assert.Contains("Nothing was restored", page);
    }

    [AzuriteFact]
    public async Task Live_revision_is_marked_and_has_no_restore_button()
    {
        using var app = new AdminApp();
        var r1 = (await app.Store.PublishAsync(WithBanner("One"), null, "sam", null)).Live!;
        await app.Store.PublishAsync(WithBanner("Two"), r1.ETag, "sam", null);
        var html = await (await app.SignIn("jane").Client.GetAsync("/History")).Content.ReadAsStringAsync();

        Assert.Contains(">Live<", html);
        Assert.Contains("restore=1", html);
        Assert.DoesNotContain("restore=2", html);
    }

    [AzuriteFact]
    public async Task Unknown_revision_is_not_found()
    {
        using var app = new AdminApp();
        var response = await app.SignIn("jane").Client.GetAsync("/History?restore=42");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
