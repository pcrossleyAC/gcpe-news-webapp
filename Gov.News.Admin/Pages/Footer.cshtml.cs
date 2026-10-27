using Gov.News.Settings;
using Microsoft.AspNetCore.Mvc;

namespace Gov.News.Admin.Pages;

public class FooterModel(BlobSiteSettingsStore store) : SettingsEditorModel(store)
{
    [BindProperty] public FooterInput Input { get; set; } = new();

    public override string SectionName => "Footer";
    protected override string ErrorPrefix => "footer.";

    protected override void LoadInput(SiteSettings source) => Input = new FooterInput
    {
        HelpText = source.Footer.HelpText,
        HelpLinks = source.Footer.HelpLinks.Select(l => l.Copy()).ToList(),
        ConnectLinks = source.Footer.ConnectLinks.Select(l => l.Copy()).ToList(),
        MoreInfoLinks = source.Footer.MoreInfoLinks.Select(l => l.Copy()).ToList(),
        CopyrightHolder = source.Footer.CopyrightHolder,
    };

    protected override void ApplyInput(SiteSettings document) => document.Footer = new FooterSettings
    {
        HelpText = (Input.HelpText ?? "").Trim(),
        HelpLinks = CleanLinks(Input.HelpLinks),
        ConnectLinks = CleanLinks(Input.ConnectLinks),
        MoreInfoLinks = CleanLinks(Input.MoreInfoLinks),
        CopyrightHolder = (Input.CopyrightHolder ?? "").Trim(),
    };

    protected override List<LinkSetting>? LinkList(string key) => key switch
    {
        nameof(FooterInput.HelpLinks) => Input.HelpLinks,
        nameof(FooterInput.ConnectLinks) => Input.ConnectLinks,
        nameof(FooterInput.MoreInfoLinks) => Input.MoreInfoLinks,
        _ => null,
    };
}

public sealed class FooterInput
{
    public string HelpText { get; set; } = "";
    public List<LinkSetting> HelpLinks { get; set; } = [];
    public List<LinkSetting> ConnectLinks { get; set; } = [];
    public List<LinkSetting> MoreInfoLinks { get; set; } = [];
    public string CopyrightHolder { get; set; } = "";
}
