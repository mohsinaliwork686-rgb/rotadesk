using System.Globalization;

using Rotadesk.Domain;

namespace Rotadesk.Api.Availability;

/// <summary>Availability times as people write them: "09:00", with "24:00" for until midnight.</summary>
internal static class TimeText
{
    private const string Midnight = "24:00";

    public static TimeOnly ParseFrom(string? text) => Parse(text);

    public static TimeOnly ParseTo(string? text) => text == Midnight ? Domain.Availability.EndOfDay : Parse(text);

    public static string Format(TimeOnly time) =>
        time == Domain.Availability.EndOfDay ? Midnight : time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static TimeOnly Parse(string? text) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw new DomainException("availability.invalid-time", $"'{text}' isn't a time like 09:00.");
}
