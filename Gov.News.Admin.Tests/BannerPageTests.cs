using System.Net;
using Gov.News.Settings;

namespace Gov.News.Admin.Tests;

public class BannerPageTests
{
    private static Dictionary<string, string> Banner(string mode, string message, params (string Text, string Url)[] links)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.Mode"] = mode,
            ["Input.Message"] = message,
            ["Input.Dismissible"] = "true",
        };
        for (var i = 0; i < links.Length; i++)
        {
            fields[$"Input.Links[{i}].Text"] = links[i].Text;
            fields[$"Input.Links[{i}].Url"] = links[i].Url;
        }
        return fields;
    }

    [AzuriteFact]
    public async Task Signed_out_visitors_cannot_open_the_page()
    {
        using var app = new AdminApp();
        var response = await app.CreateClient().GetAsync("/Banner");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [AzuriteFact]
    public async Task Shows_the_built_in_banner_before_anything_is_published()
    {
        using var app = new AdminApp();
        var editor = app.SignIn("jane.editor@gov.bc.ca");

        var response = await editor.OpenBannerAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Due to the post-election period", editor.Html);
        Assert.Contains("Nothing published yet", editor.Html);
    }

    [AzuriteFact]
    public async Task Save_draft_does_not_change_the_live_site()
    {
        using var app = new AdminApp();
        var editor = app.SignIn("jane.editor@gov.bc.ca");
        await editor.OpenBannerAsync();

        await editor.SubmitAsync("Save", Banner("info", "Office closed Monday"));

        Assert.Contains("Draft saved", editor.Html);
        Assert.Equal("Office closed Monday", (await app.Store.GetDraftAsync("jane.editor@gov.bc.ca"))!.Settings.Banner.Message);
        Assert.Null(await app.Store.GetLiveAsync());
    }

    [AzuriteFact]
    public async Task Publish_makes_it_live_with_the_editor_and_note_recorded()
    {
        using var app = new AdminApp();
        var editor = app.SignIn("jane.editor@gov.bc.ca");
        await editor.OpenBannerAsync();

        var fields = Banner("emergency", "B.C. has declared a state of provincial emergency due to regional wildfires.",
            ("EmergencyInfoBC.ca", "https://www.emergencyinfobc.gov.bc.ca"));
        fields["Note"] = "Wildfire declaration";
        await editor.SubmitAsync("Publish", fields);

        Assert.Contains("Published revision 1", editor.Html);
        var live = (await app.Store.GetLiveAsync())!.Settings;
        Assert.True(live.Banner.Enabled);
        Assert.Equal("emergency", live.Banner.Style);
        Assert.Equal("https://www.emergencyinfobc.gov.bc.ca", Assert.Single(live.Banner.Links).Url);
        Assert.Equal("jane.editor@gov.bc.ca", live.PublishedBy);
        Assert.Equal("Wildfire declaration", (await app.Store.ListHistoryAsync())[0].Note);
        Assert.Null(await app.Store.GetDraftAsync("jane.editor@gov.bc.ca"));
    }

    [AzuriteFact]
    public async Task Times_are_entered_in_Pacific_time_across_the_2026_change()
    {
        using var app = new AdminApp();
        var editor = app.SignIn("jane");
        await editor.OpenBannerAsync();

        var fields = Banner("info", "Scheduled");
        fields["Input.StartsAt"] = "2026-03-01T09:00"; // before the March 8 clock change: UTC-8
        fields["Input.EndsAt"] = "2026-11-02T09:00";   // after B.C. stays on UTC-7
        await editor.SubmitAsync("Publish", fields);

        var banner = (await app.Store.GetLiveAsync())!.Settings.Banner;
        Assert.Equal(TimeSpan.FromHours(-8), banner.StartsAt!.Value.Offset);
        Assert.Equal(TimeSpan.FromHours(-7), banner.EndsAt!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 16, 0, 0, TimeSpan.Zero), banner.EndsAt.Value.ToUniversalTime());
        // And they come back as the same wall-clock times in the form.
        Assert.Contains("value=\"2026-11-02T09:00", editor.Html);
    }

    [AzuriteFact]
    public async Task Html_in_the_message_is_rejected_and_nothing_is_published()
    {
        using var app = new AdminApp();
        var editor = app.SignIn("jane");
        await editor.OpenBannerAsync();

        await editor.SubmitAsync("Publish", Banner("info", "Click <a href=\"https://evil.example\">here</a>"));

        Assert.Contains("Use plain text", editor.Html);
        Assert.Null(await app.Store.GetLiveAsync());
    }

    [AzuriteFact]
    public async Task Unsafe_link_is_flagged_on_its_own_row()
    {
        using var app = new AdminApp();
        var editor = app.SignIn("jane");
        await editor.OpenBannerAsync();

        // A blank first row is dropped, so the http link becomes row 0 and the error must follow it.
        await editor.SubmitAsync("Publish", Banner("info", "Read more", ("", ""), ("Details", "http://gov.bc.ca")));

        Assert.Contains("Must start with https://", editor.Html);
        Assert.Contains("value=\"http://gov.bc.ca\"", editor.Html);
        Assert.Matches("id=\"Links_0_Url\"[^>]*is-invalid|is-invalid[^>]*id=\"Links_0_Url\"", editor.Html);
        Assert.Null(await app.Store.GetLiveAsync());
    }

    [AzuriteFact]
    public async Task Add_and_remove_link_rows_without_saving()
    {
        using var app = new AdminApp();
        var editor = app.SignIn("jane");
        await editor.OpenBannerAsync();

        await editor.SubmitAsync("AddLink", Banner("info", "x", ("One", "https://one.example")), "&list=Links");
        Assert.Contains("id=\"Links_1_Url\"", editor.Html);

        await editor.SubmitAsync("RemoveLink", Banner("info", "x", ("One", "https://one.example"), ("Two", "https://two.example")), "&list=Links&index=0");
        Assert.Contains("value=\"https://two.example\"", editor.Html);
        Assert.DoesNotContain("https://one.example", editor.Html);
        Assert.Null(await app.Store.GetDraftAsync("jane"));
    }

    [AzuriteFact]
    public async Task Turning_the_banner_off_keeps_its_style_for_next_time()
    {
        using var app = new AdminApp();
        var editor = app.SignIn("jane");
        await editor.OpenBannerAsync();
        await editor.SubmitAsync("Publish", Banner("emergency", "Evacuation order"));

        await editor.SubmitAsync("Publish", Banner("off", "Evacuation order"));

        var banner = (await app.Store.GetLiveAsync())!.Settings.Banner;
        Assert.False(banner.Enabled);
        Assert.Equal("emergency", banner.Style);
        Assert.Contains("id=\"mode-off\" value=\"off\" checked", editor.Html);
    }

    [AzuriteFact]
    public async Task Publishing_over_someone_elses_newer_version_is_stopped_and_explained()
    {
        using var app = new AdminApp();
        var first = await app.Store.PublishAsync(SiteSettings.Defaults(), null, "setup", null);
        var jane = app.SignIn("jane");
        await jane.OpenBannerAsync(); // Jane starts from revision 1

        // Meanwhile Sam changes the footer and publishes revision 2.
        var sams = SiteSettings.Defaults();
        sams.Footer.CopyrightHolder = "Province of British Columbia";
        await app.Store.PublishAsync(sams, first.Live!.ETag, "sam", null);

        await jane.SubmitAsync("Publish", Banner("info", "Jane's banner"));

        Assert.Contains("Someone published while you were editing", jane.Html);
        Assert.Contains("Revision 2 was published by sam", jane.Html);
        // Jane had no saved draft, so her edit was laid on Sam's version: only the banner differs.
        Assert.Contains("differs from it in: <strong>Banner</strong>", jane.Html);
        var live = (await app.Store.GetLiveAsync())!.Settings;
        Assert.Equal(2, live.Revision);
        Assert.Equal("Province of British Columbia", live.Footer.CopyrightHolder);
    }

    [AzuriteFact]
    public async Task After_a_conflict_the_editor_can_keep_their_banner_without_undoing_the_other_change()
    {
        using var app = new AdminApp();
        var first = await app.Store.PublishAsync(SiteSettings.Defaults(), null, "setup", null);
        var jane = app.SignIn("jane");
        await jane.OpenBannerAsync();
        await jane.SubmitAsync("Save", Banner("info", "Jane's banner")); // her draft holds revision 1's footer
        var sams = SiteSettings.Defaults();
        sams.Footer.CopyrightHolder = "Province of British Columbia";
        await app.Store.PublishAsync(sams, first.Live!.ETag, "sam", null);
        await jane.SubmitAsync("Publish", Banner("info", "Jane's banner"));
        Assert.Contains("differs from it in: <strong>Banner, Footer</strong>", jane.Html);

        await jane.SubmitAsync("Rebase", Banner("info", "Jane's banner"));
        Assert.Contains("Your banner changes now build on revision 2", jane.Html);
        await jane.SubmitAsync("Publish", Banner("info", "Jane's banner"));

        var live = (await app.Store.GetLiveAsync())!.Settings;
        Assert.Equal(3, live.Revision);
        Assert.Equal("Jane's banner", live.Banner.Message);
        Assert.Equal("Province of British Columbia", live.Footer.CopyrightHolder); // Sam's change survives
    }

    [AzuriteFact]
    public async Task Discard_returns_to_what_is_live()
    {
        using var app = new AdminApp();
        await app.Store.PublishAsync(SiteSettings.Defaults(), null, "setup", null);
        var editor = app.SignIn("jane");
        await editor.OpenBannerAsync();
        await editor.SubmitAsync("Save", Banner("info", "Unwanted draft"));

        await editor.SubmitAsync("Discard", Banner("info", "Unwanted draft"));

        Assert.Null(await app.Store.GetDraftAsync("jane"));
        Assert.Contains("Due to the post-election period", editor.Html);
        Assert.DoesNotContain("Unwanted draft", editor.Html);
    }

    [AzuriteFact]
    public async Task Post_without_the_antiforgery_token_is_refused()
    {
        using var app = new AdminApp();
        var editor = app.SignIn("jane");

        var response = await editor.Client.PostAsync("/Banner?handler=Publish",
            new FormUrlEncodedContent(Banner("info", "Forged")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await app.Store.GetLiveAsync());
    }
}
