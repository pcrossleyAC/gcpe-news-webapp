using Gov.News.Settings;

namespace Gov.News.Admin.Pages;

/// <param name="Title">Legend shown above the list.</param>
/// <param name="Key">Which list, as the page's LinkList(key) knows it, e.g. "MoreInfoLinks".</param>
/// <param name="Name">Form field prefix the rows bind to, e.g. "Input.MoreInfoLinks".</param>
/// <param name="ErrorField">Validator field prefix, e.g. "footer.moreInfoLinks".</param>
public sealed record LinkListView(
    string Title,
    string Key,
    string Name,
    string ErrorField,
    IReadOnlyList<LinkSetting> Links,
    SettingsEditorModel Page,
    bool Reorderable = true,
    string? Hint = null,
    bool ShowSummary = false)
{
    public IEnumerable<string> ErrorsFor(string field) => Page.ErrorsFor(field);
}
