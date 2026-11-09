using Rotadesk.Domain;

using static Rotadesk.Domain.Tests.Fixtures;

namespace Rotadesk.Domain.Tests;

public class SwapRequestTests
{
    private readonly StaffMember _amira = Person("Amira");
    private readonly StaffMember _ben = Person("Ben");
    private readonly StaffMember _manager = Person("Maya", StaffRole.Manager);
    private readonly Shift _amirasShift;
    private readonly Shift _bensShift;

    public SwapRequestTests()
    {
        _amirasShift = ShiftFor(_amira, Monday, 9, 17);
        _bensShift = ShiftFor(_ben, Monday.AddDays(1), 9, 17);
    }

    public enum Move
    {
        Accept,
        Decline,
        Cancel,
        Approve,
        Reject,
    }

    public static TheoryData<SwapStatus, Move> IllegalMoves()
    {
        var legal = new HashSet<(SwapStatus, Move)>
        {
            (SwapStatus.Requested, Move.Accept),
            (SwapStatus.Requested, Move.Decline),
            (SwapStatus.Requested, Move.Cancel),
            (SwapStatus.Accepted, Move.Cancel),
            (SwapStatus.Accepted, Move.Approve),
            (SwapStatus.Accepted, Move.Reject),
        };

        var data = new TheoryData<SwapStatus, Move>();
        foreach (var status in Enum.GetValues<SwapStatus>())
        {
            foreach (var move in Enum.GetValues<Move>())
            {
                if (!legal.Contains((status, move)))
                {
                    data.Add(status, move);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(IllegalMoves))]
    public void Every_illegal_move_is_refused(SwapStatus status, Move move)
    {
        var swap = InStatus(status);

        var error = Assert.Throws<DomainException>(() => Do(swap, move));

        Assert.Equal("swap.illegal-transition", error.Code);
        Assert.Equal(status, swap.Status);
    }

    [Fact]
    public void Approving_an_accepted_swap_moves_both_shifts()
    {
        var swap = SwapRequest.Create(_amira.Id, _ben.Id, _amirasShift, _bensShift, Now);
        swap.Accept(_ben.Id, Now);

        swap.Approve(_manager, _amirasShift, _bensShift, Now);

        Assert.Equal(SwapStatus.Approved, swap.Status);
        Assert.Equal(_ben.Id, _amirasShift.StaffMemberId);
        Assert.Equal(_amira.Id, _bensShift.StaffMemberId);
        Assert.Equal(_manager.Id, swap.DecidedById);
    }

    [Fact]
    public void A_swap_without_a_shift_in_return_only_moves_the_offered_shift()
    {
        var swap = SwapRequest.Create(_amira.Id, _ben.Id, _amirasShift, null, Now);
        swap.Accept(_ben.Id, Now);

        swap.Approve(_manager, _amirasShift, null, Now);

        Assert.Equal(_ben.Id, _amirasShift.StaffMemberId);
        Assert.Equal(_ben.Id, _bensShift.StaffMemberId);
    }

    [Fact]
    public void Only_the_colleague_can_accept_or_decline()
    {
        var swap = SwapRequest.Create(_amira.Id, _ben.Id, _amirasShift, null, Now);

        Assert.Equal("swap.wrong-person", Assert.Throws<DomainException>(() => swap.Accept(_amira.Id, Now)).Code);
        Assert.Equal("swap.wrong-person", Assert.Throws<DomainException>(() => swap.Decline(_manager.Id, Now)).Code);
        Assert.Equal(SwapStatus.Requested, swap.Status);
    }

    [Fact]
    public void Only_the_requester_can_cancel()
    {
        var swap = SwapRequest.Create(_amira.Id, _ben.Id, _amirasShift, null, Now);

        Assert.Equal("swap.wrong-person", Assert.Throws<DomainException>(() => swap.Cancel(_ben.Id, Now)).Code);

        swap.Cancel(_amira.Id, Now);
        Assert.Equal(SwapStatus.Cancelled, swap.Status);
    }

    [Fact]
    public void Staff_cannot_approve_or_reject()
    {
        var swap = Accepted();
        var carl = Person("Carl");

        Assert.Equal("swap.not-a-manager", Assert.Throws<DomainException>(() => swap.Approve(carl, _amirasShift, null, Now)).Code);
        Assert.Equal("swap.not-a-manager", Assert.Throws<DomainException>(() => swap.Reject(carl, Now)).Code);
    }

    [Fact]
    public void A_manager_from_another_business_cannot_decide()
    {
        var swap = Accepted();
        var outsider = Person("Omar", StaffRole.Manager, tenant: Guid.NewGuid());

        Assert.Equal("swap.not-a-manager", Assert.Throws<DomainException>(() => swap.Reject(outsider, Now)).Code);
    }

    [Fact]
    public void A_manager_cannot_decide_a_swap_they_are_part_of()
    {
        var managersShift = ShiftFor(_manager, Monday, 9, 17);
        var swap = SwapRequest.Create(_manager.Id, _ben.Id, managersShift, null, Now);
        swap.Accept(_ben.Id, Now);

        Assert.Equal("swap.own-swap", Assert.Throws<DomainException>(() => swap.Approve(_manager, managersShift, null, Now)).Code);
    }

    [Fact]
    public void Approval_is_refused_if_the_shift_changed_hands_since_the_request()
    {
        var swap = Accepted();
        _amirasShift.AssignTo(Person("Carl").Id);

        var error = Assert.Throws<DomainException>(() => swap.Approve(_manager, _amirasShift, null, Now));

        Assert.Equal("swap.shifts-changed", error.Code);
        Assert.Equal(SwapStatus.Accepted, swap.Status);
    }

    [Fact]
    public void You_can_only_offer_your_own_shift()
    {
        var error = Assert.Throws<DomainException>(() => SwapRequest.Create(_amira.Id, _ben.Id, _bensShift, null, Now));
        Assert.Equal("swap.not-your-shift", error.Code);
    }

    [Fact]
    public void You_cannot_swap_with_yourself()
    {
        var error = Assert.Throws<DomainException>(() => SwapRequest.Create(_amira.Id, _amira.Id, _amirasShift, null, Now));
        Assert.Equal("swap.same-person", error.Code);
    }

    [Fact]
    public void The_shift_in_return_has_to_be_the_colleagues()
    {
        var carlsShift = ShiftFor(Person("Carl"), Monday.AddDays(2), 9, 17);

        var error = Assert.Throws<DomainException>(() => SwapRequest.Create(_amira.Id, _ben.Id, _amirasShift, carlsShift, Now));
        Assert.Equal("swap.not-colleagues-shift", error.Code);
    }

    [Fact]
    public void A_shift_that_has_started_cannot_be_swapped()
    {
        var error = Assert.Throws<DomainException>(() => SwapRequest.Create(_amira.Id, _ben.Id, _amirasShift, null, _amirasShift.Start));
        Assert.Equal("swap.shift-started", error.Code);
    }

    private SwapRequest Accepted()
    {
        var swap = SwapRequest.Create(_amira.Id, _ben.Id, _amirasShift, null, Now);
        swap.Accept(_ben.Id, Now);
        return swap;
    }

    private SwapRequest InStatus(SwapStatus status)
    {
        var swap = SwapRequest.Create(_amira.Id, _ben.Id, _amirasShift, null, Now);
        switch (status)
        {
            case SwapStatus.Requested:
                break;
            case SwapStatus.Accepted:
                swap.Accept(_ben.Id, Now);
                break;
            case SwapStatus.Declined:
                swap.Decline(_ben.Id, Now);
                break;
            case SwapStatus.Cancelled:
                swap.Cancel(_amira.Id, Now);
                break;
            case SwapStatus.Approved:
                swap.Accept(_ben.Id, Now);
                swap.Approve(_manager, _amirasShift, null, Now);
                break;
            case SwapStatus.Rejected:
                swap.Accept(_ben.Id, Now);
                swap.Reject(_manager, Now);
                break;
        }

        Assert.Equal(status, swap.Status);
        return swap;
    }

    private void Do(SwapRequest swap, Move move)
    {
        switch (move)
        {
            case Move.Accept:
                swap.Accept(_ben.Id, Now);
                break;
            case Move.Decline:
                swap.Decline(_ben.Id, Now);
                break;
            case Move.Cancel:
                swap.Cancel(_amira.Id, Now);
                break;
            case Move.Approve:
                swap.Approve(_manager, _amirasShift, null, Now);
                break;
            case Move.Reject:
                swap.Reject(_manager, Now);
                break;
        }
    }
}
