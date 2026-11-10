using Rotadesk.Domain;

namespace Rotadesk.Infrastructure.Tests;

/// <summary>Two businesses with the same shape of data, saved through the system context.</summary>
internal sealed class TwoBusinesses
{
    public static readonly DateOnly Monday = new(2026, 11, 16);

    private TwoBusinesses(Business a, Business b)
    {
        A = new Seeded(a);
        B = new Seeded(b);
    }

    public Seeded A { get; }

    public Seeded B { get; }

    public static async Task<TwoBusinesses> CreateAsync(PostgresFixture fixture)
    {
        var data = new TwoBusinesses(new Business("Northside Café", "Europe/London"), new Business("Harbour Books", "Asia/Karachi"));
        await using var db = fixture.NewContext(SystemTenant.Instance);
        foreach (var s in new[] { data.A, data.B })
        {
            db.Add(s.Business);
            db.AddRange(s.Staff, s.Colleague, s.Week, s.Shift, s.ColleaguesShift, s.Window);
        }

        db.Add(data.A.Swap);
        db.Add(data.B.Swap);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return data;
    }

    internal sealed class Seeded
    {
        public Seeded(Business business)
        {
            Business = business;
            Staff = new StaffMember(business.Id, "Amira", $"amira.{business.Id:N}@example.test", StaffRole.Staff);
            Colleague = new StaffMember(business.Id, "Ben", $"ben.{business.Id:N}@example.test", StaffRole.Staff);
            Week = new RotaWeek(business.Id, Monday);
            Shift = new Shift(business.Id, Week.Id, new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 16, 17, 0, 0, TimeSpan.Zero), Staff.Id);
            ColleaguesShift = new Shift(business.Id, Week.Id, new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 17, 17, 0, 0, TimeSpan.Zero), Colleague.Id);
            Window = new Availability(business.Id, Staff.Id, DayOfWeek.Friday, new TimeOnly(18, 0), Availability.EndOfDay);
            Swap = SwapRequest.Create(Staff.Id, Colleague.Id, Shift, ColleaguesShift, new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        }

        public Business Business { get; }

        public StaffMember Staff { get; }

        public StaffMember Colleague { get; }

        public RotaWeek Week { get; }

        public Shift Shift { get; }

        public Shift ColleaguesShift { get; }

        public Availability Window { get; }

        public SwapRequest Swap { get; }
    }
}
