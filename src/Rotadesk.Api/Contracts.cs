using Rotadesk.Domain;

namespace Rotadesk.Api;

public sealed record SignInRequest(string? Email, string? Password);

public sealed record BusinessResponse(Guid Id, string Name, string TimeZone);

public sealed record MeResponse(Guid StaffMemberId, string Name, string Email, StaffRole Role, BusinessResponse Business);

public sealed record StaffResponse(Guid Id, string Name, string Email, StaffRole Role, double WeeklyLimitHours)
{
    public static StaffResponse From(StaffMember member) =>
        new(member.Id, member.Name, member.Email, member.Role, member.WeeklyLimit.TotalHours);
}

public sealed record CreateStaffRequest(string? Name, string? Email, StaffRole Role, double? WeeklyLimitHours);

/// <summary>The temporary password is shown once, to the Owner, who passes it on.</summary>
public sealed record CreatedStaffResponse(StaffResponse Staff, string TemporaryPassword);

public sealed record UpdateStaffRequest(StaffRole? Role, double? WeeklyLimitHours);

public sealed record ShiftRequest(DateTimeOffset Start, DateTimeOffset End, Guid? StaffMemberId);

public sealed record ShiftResponse(
    Guid Id,
    DateTimeOffset Start,
    DateTimeOffset End,
    Guid? StaffMemberId,
    string? StaffName,
    IReadOnlyList<string> Warnings);

public sealed record WeekResponse(DateOnly WeekStart, bool Published, DateTimeOffset? PublishedAt, IReadOnlyList<ShiftResponse> Shifts);

/// <summary>Times are "HH:mm" in the business's time zone. "24:00" as the end means until midnight.</summary>
public sealed record AvailabilityWindow(DayOfWeek Day, string From, string To);

public sealed record DemoAccount(string Email, string Name, StaffRole Role, string Business);

public sealed record DemoSignInRequest(string? Email);
