using System.Net;
using System.Text.RegularExpressions;
using Gov.News.Settings;

namespace Gov.News.Admin.Tests;

public class SocialPageTests
{
    private static Dictionary<string, string> FormFields(string html)
    {
        var fields = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(html, "<input[^>]*>"))
        {
            var name = Regex.Match(m.Value, "name=\"([^\"]+)\"").Groups[1].Value;
            var value = Regex.Match(m.Value, "value=\"([^\"]*)\"").Groups[1].Value;
            if (name.StartsWith("Input.") || name is "BaseETag" or "Network" or "__RequestVerificationToken")
                fields[WebUtility.HtmlDecode(name)] = WebUtility.HtmlDecode(value);
        }
        return fields;
    }

    [AzuriteFact]
    public async Task Each_network_opens_on_its_own_tab()
    {
        using var app = new AdminApp();
        var html = await (await app.SignIn("jane").Client.GetAsync("/Social?network=x")).Content.ReadAsStringAsync();

        Assert.Contains("value=\"https://x.com/BCGovNews\"", html);
        Assert.DoesNotContain("facebook.com", html);
    }

    [AzuriteFact]
    public async Task Adding_an_account_publishes_it_sorted_and_stays_on_the_same_network()
    {
        using var app = new AdminApp();
        var client = app.SignIn("jane").Client;
        var fields = FormFields(await (await client.GetAsync("/Social?network=bluesky")).Content.ReadAsStringAsync());
        fields["Input.Accounts[1].Text"] = "BC Parks";
        fields["Input.Accounts[1].Url"] = "https://bsky.app/profile/bcparks.bsky.social";

        var response = await client.PostAsync("/Social?handler=Publish", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("network=bluesky", response.Headers.Location!.ToString());
        var bluesky = (await app.Store.GetLiveAsync())!.Settings.Connect.Bluesky;
        Assert.Equal(["BC Parks", "Trade and Invest British Columbia"], bluesky.Accounts.Select(a => a.Text));
        Assert.Equal("Government of BC", bluesky.Featured[0].Text);
        Assert.Equal(SiteSettings.Defaults().Connect.Facebook.Accounts.Count, (await app.Store.GetLiveAsync())!.Settings.Connect.Facebook.Accounts.Count);
    }

    [AzuriteFact]
    public async Task Markup_in_a_description_is_flagged_on_its_row()
    {
        using var app = new AdminApp();
        var client = app.SignIn("jane").Client;
        var fields = FormFields(await (await client.GetAsync("/Social?network=x")).Content.ReadAsStringAsync());
        fields["Input.Featured[0].Summary"] = "<b>Breaking</b>";

        var html = await (await client.PostAsync("/Social?handler=Publish", new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync();

        Assert.Contains("Use plain text", html);
        Assert.Matches("id=\"Featured_0_Summary\"[^>]*is-invalid|is-invalid[^>]*id=\"Featured_0_Summary\"", html);
        Assert.Null(await app.Store.GetLiveAsync());
    }
}
