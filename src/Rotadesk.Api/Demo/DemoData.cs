using Rotadesk.Domain;

namespace Rotadesk.Api.Demo;

internal sealed record DemoPerson(string Name, string Email, StaffRole Role);

internal sealed record DemoBusiness(string Name, string TimeZoneId, IReadOnlyList<DemoPerson> People);

/// <summary>The people a visitor can sign in as on the live demo. They have no passwords.</summary>
internal static class DemoData
{
    public static readonly IReadOnlyList<DemoBusiness> Businesses =
    [
        new("Northside Café", "Europe/London",
        [
            new("Olivia", "olivia@northside.example", StaffRole.Owner),
            new("Marcus", "marcus@northside.example", StaffRole.Manager),
            new("Amira", "amira@northside.example", StaffRole.Staff),
            new("Ben", "ben@northside.example", StaffRole.Staff),
        ]),
        new("Harbour Books", "Asia/Karachi",
        [
            new("Sana", "sana@harbourbooks.example", StaffRole.Owner),
            new("Bilal", "bilal@harbourbooks.example", StaffRole.Manager),
            new("Hira", "hira@harbourbooks.example", StaffRole.Staff),
            new("Usman", "usman@harbourbooks.example", StaffRole.Staff),
        ]),
    ];

    public static bool IsDemoAccount(string? email) =>
        email is not null && Businesses.Any(b => b.People.Any(p => string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase)));
}
