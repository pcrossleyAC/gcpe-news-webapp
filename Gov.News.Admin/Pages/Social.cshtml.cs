using Gov.News.Settings;
using Microsoft.AspNetCore.Mvc;

namespace Gov.News.Admin.Pages;

/// <summary>Accounts on the public Connect page, one network at a time.</summary>
public class SocialModel(BlobSiteSettingsStore store) : SettingsEditorModel(store)
{
    /// <summary>Which network is open, e.g. "facebook". Comes from the query string, and from the form on post.</summary>
    [BindProperty(SupportsGet = true)] public string Network { get; set; } = "facebook";
    [BindProperty] public SocialInput Input { get; set; } = new();

    public override string SectionName => "Social links";
    protected override string ErrorPrefix => "connect.";
    protected override object? RouteValues => new { network = Network };

    public string NetworkName => Current.Connect.Networks().FirstOrDefault(n => n.Key == Network).Name ?? Network;

    protected override void LoadInput(SiteSettings source)
    {
        var links = Find(source);
        Input = new SocialInput
        {
            Featured = links.Featured.Select(l => l.Copy()).ToList(),
            Accounts = links.Accounts.OrderBy(a => a.Text).Select(l => l.Copy()).ToList(),
        };
    }

    protected override void ApplyInput(SiteSettings document)
    {
        var links = Find(document);
        links.Featured = CleanLinks(Input.Featured);
        links.Accounts = CleanLinks(Input.Accounts).OrderBy(a => a.Text).ToList();
    }

    protected override List<LinkSetting>? LinkList(string key) => key switch
    {
        nameof(SocialInput.Featured) => Input.Featured,
        nameof(SocialInput.Accounts) => Input.Accounts,
        _ => null,
    };

    private SocialNetworkLinks Find(SiteSettings settings)
    {
        var match = settings.Connect.Networks().FirstOrDefault(n => n.Key == Network);
        if (match.Links is null)
        {
            Network = "facebook";
            return settings.Connect.Facebook;
        }
        return match.Links;
    }
}

public sealed class SocialInput
{
    public List<LinkSetting> Featured { get; set; } = [];
    public List<LinkSetting> Accounts { get; set; } = [];
}
