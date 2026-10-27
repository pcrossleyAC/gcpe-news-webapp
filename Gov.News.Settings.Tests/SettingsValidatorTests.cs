namespace Gov.News.Settings.Tests;

public class SettingsValidatorTests
{
    [Fact]
    public void Defaults_are_publishable()
    {
        Assert.Empty(SettingsValidator.Validate(SiteSettings.Defaults()));
    }

    [Fact]
    public void Defaults_match_the_live_site()
    {
        var defaults = SiteSettings.Defaults();
        Assert.True(defaults.Banner.Enabled);
        Assert.StartsWith("Due to the post-election period", defaults.Banner.Message);
        Assert.Equal("#013366", defaults.Theme.Colors.Primary);
        Assert.Equal(6, defaults.Footer.MoreInfoLinks.Count);
        Assert.Equal("Government of British Columbia", defaults.Footer.CopyrightHolder);
    }

    [Theory]
    [InlineData("#000000", "#FFFFFF", 21.0)]
    [InlineData("#FFFFFF", "#FFFFFF", 1.0)]
    [InlineData("#013366", "#FFFFFF", 12.6)]
    [InlineData("#CE3E39", "#FFFFFF", 4.8)]
    public void Contrast_ratio_matches_WCAG(string a, string b, double expected)
    {
        Assert.Equal(expected, SettingsValidator.ContrastRatio(a, b), precision: 1);
    }

    [Theory]
    [InlineData("https://gov.bc.ca")]
    [InlineData("https://www2.gov.bc.ca/gov/admin/disclaimer.page")]
    [InlineData("/contact")]
    public void Safe_urls_are_accepted(string url)
    {
        Assert.True(SettingsValidator.IsSafeUrl(url));
    }

    [Theory]
    [InlineData("http://www2.gov.bc.ca/gov/content/home/privacy")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData(" https://gov.bc.ca")]
    [InlineData("gov.bc.ca")]
    [InlineData("")]
    public void Unsafe_urls_are_rejected(string url)
    {
        Assert.False(SettingsValidator.IsSafeUrl(url));
    }

    [Fact]
    public void Http_footer_link_is_reported_against_its_field()
    {
        var settings = SiteSettings.Defaults();
        settings.Footer.MoreInfoLinks[2].Url = "http://www2.gov.bc.ca/gov/content/home/privacy";

        var error = Assert.Single(SettingsValidator.Validate(settings));
        Assert.Equal("footer.moreInfoLinks[2].url", error.Field);
    }

    [Fact]
    public void Banner_message_cannot_contain_markup()
    {
        var settings = SiteSettings.Defaults();
        settings.Banner.Message = "Evacuate <a href=\"https://example.com\">now</a>";

        Assert.Contains(SettingsValidator.Validate(settings), e => e.Field == "banner.message");
    }

    [Fact]
    public void Enabled_banner_needs_a_message()
    {
        var settings = SiteSettings.Defaults();
        settings.Banner.Message = "  ";

        Assert.Contains(SettingsValidator.Validate(settings), e => e.Field == "banner.message");
    }

    [Fact]
    public void Banner_must_end_after_it_starts()
    {
        var settings = SiteSettings.Defaults();
        settings.Banner.StartsAt = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(-7));
        settings.Banner.EndsAt = settings.Banner.StartsAt.Value.AddHours(-1);

        Assert.Contains(SettingsValidator.Validate(settings), e => e.Field == "banner.endsAt");
    }

    [Fact]
    public void Faint_emergency_banner_colour_is_rejected()
    {
        var settings = SiteSettings.Defaults();
        settings.Theme.Colors.BannerEmergency = "#F08080"; // light coral: about 2.4:1 with white text

        var error = Assert.Single(SettingsValidator.Validate(settings));
        Assert.Equal("theme.colors.bannerEmergency", error.Field);
        Assert.Contains("2.", error.Message);
    }

    [Fact]
    public void Footer_background_is_checked_against_body_text()
    {
        var settings = SiteSettings.Defaults();
        settings.Theme.Colors.FooterBackground = "#555555";

        Assert.Contains(SettingsValidator.Validate(settings), e => e.Field == "theme.colors.footerBackground");
    }

    [Theory]
    [InlineData("013366")]
    [InlineData("#013")]
    [InlineData("#01336G")]
    [InlineData("red")]
    public void Malformed_colours_are_rejected(string value)
    {
        var settings = SiteSettings.Defaults();
        settings.Theme.Colors.Link = value;

        Assert.Contains(SettingsValidator.Validate(settings), e => e.Field == "theme.colors.link");
    }

    [Fact]
    public void Scheduled_banner_shows_only_inside_its_window()
    {
        var start = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(-7));
        var banner = new BannerSettings { Enabled = true, Message = "x", StartsAt = start, EndsAt = start.AddDays(1) };

        Assert.False(banner.IsVisibleAt(start.AddSeconds(-1)));
        Assert.True(banner.IsVisibleAt(start));
        Assert.False(banner.IsVisibleAt(start.AddDays(1)));
    }
}

public class SchemaUpgradeTests
{
    [Fact]
    public void Version_1_document_keeps_its_content_and_gets_the_default_social_links_and_theme()
    {
        // Shape published by the first admin builds: social lists were flat arrays and unused.
        var v1 = SiteSettings.Defaults();
        v1.Banner.Message = "Published under version 1";
        var json = System.Text.Json.Nodes.JsonNode.Parse(v1.ToJson())!.AsObject();
        json["schemaVersion"] = 1;
        json["connect"] = System.Text.Json.Nodes.JsonNode.Parse("""{"facebook":[],"x":[],"instagram":[],"youtube":[]}""");
        json["theme"]!["colors"]!["link"] = "#255A90"; // version 1 sample value, never applied to the site

        var upgraded = SiteSettings.FromJson(json.ToJsonString());

        Assert.Equal(SiteSettings.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.Equal("Published under version 1", upgraded.Banner.Message);
        Assert.Equal("Government of British Columbia", upgraded.Connect.Facebook.Featured[0].Text);
        Assert.Equal("#003366", upgraded.Theme.Colors.Link);
        Assert.Empty(SettingsValidator.Validate(upgraded));
    }

    [Fact]
    public void Default_social_links_match_the_lists_that_were_in_the_code()
    {
        var connect = SiteSettings.Defaults().Connect;
        Assert.Equal(83, connect.Networks().Sum(n => n.Links.Featured.Count + n.Links.Accounts.Count));
        Assert.Equal(["Government of British Columbia", "Government of British Columbia Chinese (卑詩省政府中文官方帳號)"],
            connect.Facebook.Featured.Select(l => l.Text));
        Assert.Equal("@BCGovNews", connect.X.InDisplayOrder().First().Text);
        Assert.Equal("Read daily news tweets from the Government of British Columbia", connect.X.Featured[0].Summary);
    }

    [Fact]
    public void Social_summary_cannot_contain_markup()
    {
        var settings = SiteSettings.Defaults();
        settings.Connect.X.Accounts[0].Summary = "<script>alert(1)</script>";
        Assert.Contains(SettingsValidator.Validate(settings), e => e.Field == "connect.x.accounts[0].summary");
    }
}
