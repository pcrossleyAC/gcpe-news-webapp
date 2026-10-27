using System.Net;
using System.Text.RegularExpressions;
using Gov.News.Settings;

namespace Gov.News.Admin.Tests;

public class ThemePageTests
{
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
        return fields;
    }

    [AzuriteFact]
    public async Task Shows_the_sites_real_colours()
    {
        using var app = new AdminApp();
        var html = await (await app.SignIn("jane").Client.GetAsync("/Theme")).Content.ReadAsStringAsync();

        Assert.Contains("name=\"Input.Link\" value=\"#003366\"", html);
        Assert.Contains("name=\"Input.Text\" value=\"#2d2d2d\"", html);
        Assert.Contains("name=\"Input.FooterBackground\" value=\"#ffffff\"", html);
    }

    [AzuriteFact]
    public async Task Publishing_a_darker_link_colour_stores_it_upper_case()
    {
        using var app = new AdminApp();
        var client = app.SignIn("jane").Client;
        var fields = FormFields(await (await client.GetAsync("/Theme")).Content.ReadAsStringAsync());
        fields["Input.Link"] = "#1a5a96";

        var response = await client.PostAsync("/Theme?handler=Publish", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var live = (await app.Store.GetLiveAsync())!.Settings;
        Assert.Equal("#1A5A96", live.Theme.Colors.Link);
        Assert.Equal("#013366", live.Theme.Colors.Primary);
    }

    [AzuriteFact]
    public async Task Faint_colour_is_refused_with_the_reason_on_its_row()
    {
        using var app = new AdminApp();
        var client = app.SignIn("jane").Client;
        var fields = FormFields(await (await client.GetAsync("/Theme")).Content.ReadAsStringAsync());
        fields["Input.Text"] = "#aaaaaa";

        var html = await (await client.PostAsync("/Theme?handler=Publish", new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync();

        Assert.Contains("Too faint to read on white", WebUtility.HtmlDecode(html));
        Assert.Null(await app.Store.GetLiveAsync());
    }

    [AzuriteFact]
    public async Task Reset_puts_the_defaults_back_in_the_form_without_publishing()
    {
        using var app = new AdminApp();
        var client = app.SignIn("jane").Client;
        var fields = FormFields(await (await client.GetAsync("/Theme")).Content.ReadAsStringAsync());
        fields["Input.Link"] = "#5a1a96";

        var html = await (await client.PostAsync("/Theme?handler=Reset", new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync();

        Assert.Contains("name=\"Input.Link\" value=\"#003366\"", html);
        Assert.Null(await app.Store.GetLiveAsync());
    }
}
