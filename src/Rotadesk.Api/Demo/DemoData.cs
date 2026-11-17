using Rotadesk.Domain;

namespace Rotadesk.Api.Demo;

/// <summary>A time someone can work each week, in the business's time.</summary>
internal sealed record DemoWindow(DayOfWeek Day, string From, string To);

internal sealed record DemoPerson(
    string Name,
    string Email,
    StaffRole Role,
    int WeeklyLimitHours = 40,
    IReadOnlyList<DemoWindow>? Availability = null);

/// <summary>A shift in the demo's typical week. The day counts from Monday, 0 to 6; no email is an open shift.</summary>
internal sealed record DemoShift(int Day, string Start, string End, string? Email);

/// <summary>A swap waiting in the current week: someone offers their shift, maybe for one of the colleague's.</summary>
internal sealed record DemoSwap(string From, DemoShift Offered, string To, DemoShift? Return, bool Accepted);

internal sealed record DemoBusiness(
    string Name,
    string TimeZoneId,
    IReadOnlyList<DemoPerson> People,
    IReadOnlyList<DemoShift> Week,
    IReadOnlyList<DemoSwap> Swaps);

/// <summary>
/// The two made-up businesses on the live demo, in different time zones. People have no passwords; a visitor signs
/// in as one with a click. Each typical week keeps to the scheduling rules, and a test checks that it does.
/// </summary>
internal static class DemoData
{
    private const string Amira = "amira@northside.example";
    private const string Ben = "ben@northside.example";
    private const string Marcus = "marcus@northside.example";
    private const string Sana = "sana@harbourbooks.example";
    private const string Bilal = "bilal@harbourbooks.example";
    private const string Hira = "hira@harbourbooks.example";
    private const string Usman = "usman@harbourbooks.example";

    private static readonly DemoShift BenSaturday = new(5, "08:00", "16:00", Ben);
    private static readonly DemoShift UsmanWednesday = new(2, "10:00", "18:00", Usman);
    private static readonly DemoShift HiraThursday = new(3, "10:00", "16:00", Hira);

    public static readonly IReadOnlyList<DemoBusiness> Businesses =
    [
        new(
            "Northside Café",
            "Europe/London",
            [
                new("Olivia", "olivia@northside.example", StaffRole.Owner),
                new("Marcus", Marcus, StaffRole.Manager),
                // Part-time, and not free before 08:00 on Fridays, so Friday's early shift shows a warning.
                new("Amira", Amira, StaffRole.Staff, 32,
                [
                    new(DayOfWeek.Monday, "07:00", "16:00"),
                    new(DayOfWeek.Wednesday, "07:00", "16:00"),
                    new(DayOfWeek.Friday, "08:00", "16:00"),
                    new(DayOfWeek.Saturday, "10:00", "18:00"),
                ]),
                new("Ben", Ben, StaffRole.Staff),
            ],
            [
                new(0, "07:00", "15:00", Amira),
                new(0, "11:00", "19:00", Ben),
                new(1, "07:00", "15:00", Ben),
                new(1, "11:00", "19:00", Marcus),
                new(2, "07:00", "15:00", Amira),
                new(2, "11:00", "19:00", Ben),
                new(3, "07:00", "15:00", Ben),
                new(3, "11:00", "19:00", Marcus),
                new(4, "07:00", "15:00", Amira),
                new(4, "11:00", "19:00", Marcus),
                BenSaturday,
                new(5, "12:00", "19:00", null),
                new(6, "09:00", "17:00", Marcus),
            ],
            // Amira has agreed to take Ben's Saturday, so a manager has a swap to decide.
            [new(Ben, BenSaturday, Amira, null, Accepted: true)]),
        new(
            "Harbour Books",
            "Asia/Karachi",
            [
                new("Sana", Sana, StaffRole.Owner),
                new("Bilal", Bilal, StaffRole.Manager),
                new("Hira", Hira, StaffRole.Staff, 24,
                [
                    new(DayOfWeek.Monday, "10:00", "16:00"),
                    new(DayOfWeek.Tuesday, "10:00", "16:00"),
                    new(DayOfWeek.Thursday, "10:00", "16:00"),
                    new(DayOfWeek.Saturday, "10:00", "18:00"),
                ]),
                new("Usman", Usman, StaffRole.Staff),
            ],
            [
                new(0, "10:00", "16:00", Hira),
                new(0, "14:00", "20:00", Usman),
                new(1, "10:00", "16:00", Hira),
                new(1, "14:00", "20:00", Bilal),
                UsmanWednesday,
                new(2, "14:00", "20:00", null),
                HiraThursday,
                new(3, "14:00", "20:00", Bilal),
                new(4, "10:00", "16:00", Sana),
                new(4, "12:00", "20:00", Usman),
                new(5, "10:00", "18:00", Usman),
                new(5, "12:00", "20:00", Bilal),
            ],
            // Usman asks Hira to trade his Wednesday for her Thursday; it waits for her answer.
            [new(Usman, UsmanWednesday, Hira, HiraThursday, Accepted: false)]),
    ];

    public static bool IsDemoAccount(string? email) =>
        email is not null && Businesses.Any(b => b.People.Any(p => string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase)));
}
