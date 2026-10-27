using System.Net;
using System.Text.RegularExpressions;
using Gov.News.Settings;

namespace Gov.News.Admin.Tests;

public class FooterPageTests
{
    /// <summary>Every field of the footer form as the page rendered it, so a test can change one thing and post the rest unchanged.</summary>
    private static Dictionary<string, string> FormFields(string html)
    {
        var fields = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(html, "<input[^>]*>"))
        {
            var name = Regex.Match(m.Value, "name=\"([^\"]+)\"").Groups[1].Value;
            var value = Regex.Match(m.Value, "value=\"([^\"]*)\"").Groups[1].Value;
            if (name.StartsWith("Input.") || name is "BaseETag" or "__RequestVerificationToken")
                fields[WebUtility.HtmlDecode(name)] = WebUtility.HtmlDecode(value);
        }
        var helpText = Regex.Match(html, "<textarea[^>]*name=\"Input.HelpText\"[^>]*>([^<]*)</textarea>");
        fields["Input.HelpText"] = WebUtility.HtmlDecode(helpText.Groups[1].Value).Trim();
        return fields;
    }

    private static async Task<string> PostAsync(HttpClient client, string query, Dictionary<string, string> fields)
    {
        var response = await client.PostAsync("/Footer?" + query, new FormUrlEncodedContent(fields));
        if (response.StatusCode == HttpStatusCode.Redirect)
            response = await client.GetAsync("/Footer");
        return await response.Content.ReadAsStringAsync();
    }

    [AzuriteFact]
    public async Task Footer_page_shows_the_current_links()
    {
        using var app = new AdminApp();
        var html = await (await app.SignIn("jane").Client.GetAsync("/Footer")).Content.ReadAsStringAsync();

        Assert.Contains("value=\"https://www2.gov.bc.ca/gov/admin/disclaimer.page\"", html);
        Assert.Contains("value=\"footerDisclaimer\"", html); // id carried as a hidden field
        Assert.Contains("We can help in over 220 languages", html);
    }

    [AzuriteFact]
    public async Task Editing_a_link_publishes_it_and_keeps_the_ids_and_the_banner()
    {
        using var app = new AdminApp();
        var bannerBefore = SiteSettings.Defaults().Banner.Message;
        var client = app.SignIn("jane").Client;
        var fields = FormFields(await (await client.GetAsync("/Footer")).Content.ReadAsStringAsync());
        fields["Input.MoreInfoLinks[5].Text"] = "Contact the Newsroom";
        fields["Input.CopyrightHolder"] = "Province of British Columbia";

        var html = await PostAsync(client, "handler=Publish", fields);

        Assert.Contains("Published revision 1", html);
        var live = (await app.Store.GetLiveAsync())!.Settings;
        Assert.Equal("Contact the Newsroom", live.Footer.MoreInfoLinks[5].Text);
        Assert.Equal("footerContact", live.Footer.MoreInfoLinks[5].Id);
        Assert.Equal("footerHome", live.Footer.MoreInfoLinks[0].Id);
        Assert.Equal("Province of British Columbia", live.Footer.CopyrightHolder);
        Assert.Equal(bannerBefore, live.Banner.Message);
    }

    [AzuriteFact]
    public async Task Moving_a_link_down_reorders_it_without_saving()
    {
        using var app = new AdminApp();
        var client = app.SignIn("jane").Client;
        var fields = FormFields(await (await client.GetAsync("/Footer")).Content.ReadAsStringAsync());

        var html = await PostAsync(client, "handler=MoveLink&list=MoreInfoLinks&index=0&by=1", fields);

        Assert.True(html.IndexOf("value=\"Disclaimer\"") < html.IndexOf("value=\"BC Gov\""));
        Assert.Matches("name=\"Input.MoreInfoLinks\\[1\\].Id\" value=\"footerHome\"", html); // id moves with its link
        Assert.Null(await app.Store.GetLiveAsync());
    }

    [AzuriteFact]
    public async Task Http_link_is_flagged_and_not_published()
    {
        using var app = new AdminApp();
        var client = app.SignIn("jane").Client;
        var fields = FormFields(await (await client.GetAsync("/Footer")).Content.ReadAsStringAsync());
        fields["Input.MoreInfoLinks[2].Url"] = "http://www2.gov.bc.ca/gov/content/home/privacy";

        var html = await PostAsync(client, "handler=Publish", fields);

        Assert.Contains("Must start with https://", html);
        Assert.Matches("id=\"MoreInfoLinks_2_Url\"[^>]*is-invalid|is-invalid[^>]*id=\"MoreInfoLinks_2_Url\"", html);
        Assert.Null(await app.Store.GetLiveAsync());
    }

    [AzuriteFact]
    public async Task Forged_link_id_cannot_inject_markup()
    {
        using var app = new AdminApp();
        var client = app.SignIn("jane").Client;
        var fields = FormFields(await (await client.GetAsync("/Footer")).Content.ReadAsStringAsync());
        fields["Input.MoreInfoLinks[0].Id"] = "x\" onclick=\"alert(1)";

        var html = await PostAsync(client, "handler=Publish", fields);

        Assert.Contains("Link id must start with a letter", html);
        Assert.Null(await app.Store.GetLiveAsync());
    }
}
