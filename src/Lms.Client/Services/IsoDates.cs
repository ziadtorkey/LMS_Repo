using System.Globalization;

namespace Lms.Client.Services;

/// <summary>ISO date-times (yyyy-MM-dd HH:mm, local time) isolated left-to-right so the digits keep their order inside Arabic text.</summary>
public static class IsoDates
{
    public static string Format(DateTime utc) =>
        "⁦" + DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "⁩";

    public static string? Format(DateTime? utc) => utc is { } v ? Format(v) : null;
}
