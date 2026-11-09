using Rotadesk.Domain;

using static Rotadesk.Domain.Tests.Fixtures;

namespace Rotadesk.Domain.Tests;

public class SwapApprovalTests
{
    private static ScheduleCheck CheckSwap(SwapRequest swap, Shift offered, Shift? returned, StaffMember requester, StaffMember colleague, params Shift[] others) =>
        SwapApproval.Check(swap, offered, returned, requester, colleague, [offered, .. returned is null ? Array.Empty<Shift>() : [returned], .. others], [], London);

    [Fact]
    public void A_swap_that_would_double_book_the_colleague_is_refused()
    {
        var amira = Person("Amira");
        var ben = Person("Ben");
        var offered = ShiftFor(amira, Monday, 9, 17);
        var bensOtherShift = ShiftFor(ben, Monday, 12, 20);
        var swap = SwapRequest.Create(amira.Id, ben.Id, offered, null, Now);
        swap.Accept(ben.Id, Now);

        var check = CheckSwap(swap, offered, null, amira, ben, bensOtherShift);

        Assert.Equal("shift.overlap", Assert.Single(check.Violations).Code);
    }

    [Fact]
    public void A_straight_swap_of_two_shifts_at_the_same_time_is_allowed()
    {
        var amira = Person("Amira");
        var ben = Person("Ben");
        var offered = ShiftFor(amira, Monday, 9, 17);
        var returned = ShiftFor(ben, Monday, 9, 17);
        var swap = SwapRequest.Create(amira.Id, ben.Id, offered, returned, Now);

        Assert.True(CheckSwap(swap, offered, returned, amira, ben).IsAllowed);
    }

    [Fact]
    public void A_swap_that_would_take_the_colleague_over_their_limit_is_refused()
    {
        var amira = Person("Amira");
        var ben = Person("Ben", limitHours: 8);
        var offered = ShiftFor(amira, Monday, 9, 17);
        var bensTuesday = ShiftFor(ben, Monday.AddDays(1), 9, 17);
        var swap = SwapRequest.Create(amira.Id, ben.Id, offered, null, Now);

        var check = CheckSwap(swap, offered, null, amira, ben, bensTuesday);

        Assert.Equal("shift.weekly-limit", Assert.Single(check.Violations).Code);
    }

    [Fact]
    public void Giving_a_shift_back_frees_its_hours()
    {
        var amira = Person("Amira");
        var ben = Person("Ben", limitHours: 8);
        var offered = ShiftFor(amira, Monday, 9, 17);
        var bensTuesday = ShiftFor(ben, Monday.AddDays(1), 9, 17);
        var swap = SwapRequest.Create(amira.Id, ben.Id, offered, bensTuesday, Now);

        Assert.True(CheckSwap(swap, offered, bensTuesday, amira, ben).IsAllowed);
    }

    [Fact]
    public void The_requester_is_checked_for_the_shift_they_take_in_return()
    {
        var amira = Person("Amira");
        var ben = Person("Ben");
        var offered = ShiftFor(amira, Monday, 9, 17);
        var returned = ShiftFor(ben, Monday.AddDays(1), 9, 17);
        var amirasTuesday = ShiftFor(amira, Monday.AddDays(1), 13, 21);
        var swap = SwapRequest.Create(amira.Id, ben.Id, offered, returned, Now);

        var check = CheckSwap(swap, offered, returned, amira, ben, amirasTuesday);

        Assert.Equal("shift.overlap", Assert.Single(check.Violations).Code);
    }
}
