namespace Gov.News.Admin;

/// <summary>
/// Editors enter times as B.C. wall-clock time; settings store them as offsets.
/// Uses the IANA zone so the 2026 change to permanent UTC-7 comes from tzdata, not code.
/// </summary>
public static class PacificTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Vancouver");

    public static DateTimeOffset? FromLocal(DateTime? local)
    {
        if (local is null)
            return null;
        var wallClock = DateTime.SpecifyKind(local.Value, DateTimeKind.Unspecified);
        return new DateTimeOffset(wallClock, Zone.GetUtcOffset(wallClock));
    }

    public static DateTime? ToLocal(DateTimeOffset? value) =>
        value is null ? null : TimeZoneInfo.ConvertTime(value.Value, Zone).DateTime;

    public static string Format(DateTimeOffset? value) =>
        value is null ? "" : TimeZoneInfo.ConvertTime(value.Value, Zone).ToString("MMM d, yyyy h:mm tt");
}
