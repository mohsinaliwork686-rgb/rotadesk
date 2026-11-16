using System.Net;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using static Rotadesk.Api.Tests.Http;

namespace Rotadesk.Api.Tests;

/// <summary>
/// Every endpoint, in one table: who may call it and, for those that take an id, what kind. The first test checks
/// the table against the app itself, so a new endpoint can't skip the role and isolation checks below.
/// </summary>
public class EndpointSuiteTests(ApiFixture fixture)
{
    private const string Public = "public";

    private enum Id
    {
        None,
        Staff,
        Shift,
        Swap,
    }

    private sealed record Endpoint(string Access, Id Kind = Id.None, Func<object>? Body = null);

    private static readonly Dictionary<(string Method, string Pattern), Endpoint> Endpoints = new()
    {
        [("GET", "/health")] = new(Public),
        [("POST", "/api/auth/sign-in")] = new(Public),
        [("POST", "/api/auth/sign-out")] = new(Public),
        [("GET", "/api/auth/me")] = new(Policies.Member),
        [("GET", "/api/demo/accounts")] = new(Public),
        [("POST", "/api/demo/sign-in")] = new(Public),
        [("GET", "/api/inbox")] = new(Policies.Member),
        [("GET", "/api/staff")] = new(Policies.Member),
        [("POST", "/api/staff")] = new(Policies.Owner, Body: () => new { name = "Cara", email = $"cara.{Guid.NewGuid():N}@example.test", role = "Staff" }),
        [("PATCH", "/api/staff/{id:guid}")] = new(Policies.Owner, Id.Staff, () => new { weeklyLimitHours = 30 }),
        [("GET", "/api/staff/{id:guid}/availability")] = new(Policies.Member, Id.Staff),
        [("GET", "/api/weeks/{weekStart}")] = new(Policies.Member),
        [("POST", "/api/weeks/{weekStart}/publish")] = new(Policies.Manager),
        [("POST", "/api/weeks/{weekStart}/copy-previous")] = new(Policies.Manager),
        [("POST", "/api/shifts")] = new(Policies.Manager, Body: () => new { start = At(Monday, 9), end = At(Monday, 17) }),
        [("PUT", "/api/shifts/{id:guid}")] = new(Policies.Manager, Id.Shift, () => new { start = At(Monday, 10), end = At(Monday, 18) }),
        [("DELETE", "/api/shifts/{id:guid}")] = new(Policies.Manager, Id.Shift),
        [("GET", "/api/availability/mine")] = new(Policies.Member),
        [("PUT", "/api/availability/mine")] = new(Policies.Member, Body: () => Array.Empty<object>()),
        [("GET", "/api/swaps")] = new(Policies.Member),
        [("GET", "/api/swaps/{id:guid}")] = new(Policies.Member, Id.Swap),
        [("POST", "/api/swaps")] = new(Policies.Member, Body: () => new { offeredShiftId = Guid.NewGuid(), colleagueId = Guid.NewGuid() }),
        [("POST", "/api/swaps/{id:guid}/accept")] = new(Policies.Member, Id.Swap),
        [("POST", "/api/swaps/{id:guid}/decline")] = new(Policies.Member, Id.Swap),
        [("POST", "/api/swaps/{id:guid}/cancel")] = new(Policies.Member, Id.Swap),
        [("POST", "/api/swaps/{id:guid}/reject")] = new(Policies.Manager, Id.Swap),
        [("POST", "/api/swaps/{id:guid}/approve")] = new(Policies.Manager, Id.Swap),

        // The fallbacks: a 404 for any unknown API path, and the React app for every other page.
        [("*", "/api/{*path:nonfile}")] = new(Public),
        [("GET,HEAD", "{*path:nonfile}")] = new(Public),
    };

    public static TheoryData<string, string> Protected() => Select(e => e.Access != Public);

    public static TheoryData<string, string> ForManagers() => Select(e => e.Access == Policies.Manager);

    public static TheoryData<string, string> ForOwners() => Select(e => e.Access == Policies.Owner);

    public static TheoryData<string, string> TakingAnId() => Select(e => e.Kind != Id.None);

    [Fact]
    public void The_table_lists_every_endpoint_the_app_has_with_its_access()
    {
        var actual = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (
                Method: e.Metadata.GetMetadata<HttpMethodMetadata>() is { } methods ? string.Join(",", methods.HttpMethods) : "*",
                // A route mapped as "" inside a group can come out with a trailing slash; routing treats both the same.
                Pattern: e.RoutePattern.RawText!.TrimEnd('/'),
                Access: e.Metadata.GetMetadata<IAllowAnonymous>() is not null
                    ? Public
                    : e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).LastOrDefault() ?? Public))
            .OrderBy(e => e.Pattern).ThenBy(e => e.Method)
            .ToList();

        var expected = Endpoints.Select(e => (e.Key.Method, e.Key.Pattern, e.Value.Access)).OrderBy(e => e.Pattern).ThenBy(e => e.Method).ToList();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Protected))]
    public async Task Without_signing_in_every_protected_endpoint_is_a_401(string method, string pattern)
    {
        var response = await SendAsync(fixture.NewClient(), method, pattern, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ForManagers))]
    public async Task Staff_get_a_403_from_every_manager_endpoint(string method, string pattern)
    {
        var business = await fixture.CreateBusinessAsync();

        var response = await SendAsync(await fixture.SignInAsync(business.Staff), method, pattern, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ForOwners))]
    public async Task Managers_get_a_403_from_every_owner_endpoint(string method, string pattern)
    {
        var business = await fixture.CreateBusinessAsync();

        var response = await SendAsync(await fixture.SignInAsync(business.Manager), method, pattern, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Business A's owner, who passes every policy, aims at business B's real ids. Only the tenant filter stands in
    /// the way, and the answer must be a 404, so an id can't even be shown to exist.
    /// </summary>
    [Theory]
    [MemberData(nameof(TakingAnId))]
    public async Task Another_businesss_id_is_a_404_on_every_endpoint_that_takes_one(string method, string pattern)
    {
        var a = await fixture.CreateBusinessAsync();
        var b = await BusinessWithASwapAsync();
        var id = Endpoints[(method, pattern)].Kind switch
        {
            Id.Staff => b.Business.Staff.Id,
            Id.Shift => b.Shift.Id,
            _ => b.Swap.Id,
        };

        var response = await SendAsync(await fixture.SignInAsync(a.Owner), method, pattern, id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_request_naming_another_businesss_people_or_shifts_is_a_404()
    {
        var a = await BusinessWithASwapAsync();
        var b = await BusinessWithASwapAsync();
        var manager = await fixture.SignInAsync(a.Business.Manager);
        var staff = await fixture.SignInAsync(a.Business.Staff);

        HttpResponseMessage[] responses =
        [
            await manager.PostJsonAsync("/api/shifts", new { start = At(Monday, 9), end = At(Monday, 12), staffMemberId = b.Business.Staff.Id }),
            await manager.PutJsonAsync($"/api/shifts/{a.Shift.Id}", new { start = At(Monday, 9), end = At(Monday, 17), staffMemberId = b.Business.Colleague.Id }),
            await staff.PostJsonAsync("/api/swaps", new { offeredShiftId = b.Shift.Id, colleagueId = a.Business.Colleague.Id }),
            await staff.PostJsonAsync("/api/swaps", new { offeredShiftId = a.Shift.Id, colleagueId = b.Business.Colleague.Id }),
            await staff.PostJsonAsync("/api/swaps", new { offeredShiftId = a.Shift.Id, colleagueId = a.Business.Colleague.Id, returnShiftId = b.Shift.Id }),
        ];

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
    }

    private sealed record Seeded(TestBusiness Business, ShiftResponse Shift, SwapResponse Swap);

    /// <summary>A business with a published week, Amira's Monday shift, and her swap request to Ben.</summary>
    private async Task<Seeded> BusinessWithASwapAsync()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        var shift = await manager.AddShiftAsync(business.Staff, At(Monday, 9), At(Monday, 17));
        await manager.PostJsonAsync($"/api/weeks/{Monday:yyyy-MM-dd}/publish");
        var asked = await (await fixture.SignInAsync(business.Staff)).PostJsonAsync("/api/swaps", new { offeredShiftId = shift.Id, colleagueId = business.Colleague.Id });
        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        return new Seeded(business, shift, await asked.ReadAsync<SwapResponse>());
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string pattern, Guid id)
    {
        var url = pattern.Replace("{id:guid}", id.ToString(), StringComparison.Ordinal).Replace("{weekStart}", $"{Monday:yyyy-MM-dd}", StringComparison.Ordinal);
        var body = Endpoints[(method, pattern)].Body?.Invoke();
        return method switch
        {
            "GET" => client.GetAsync(url, TestContext.Current.CancellationToken),
            "DELETE" => client.DeleteAsync(url, TestContext.Current.CancellationToken),
            "POST" => client.PostJsonAsync(url, body),
            "PUT" => client.PutJsonAsync(url, body ?? new { }),
            "PATCH" => client.PatchJsonAsync(url, body ?? new { }),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };
    }

    private static TheoryData<string, string> Select(Func<Endpoint, bool> which)
    {
        var data = new TheoryData<string, string>();
        foreach (var ((method, pattern), endpoint) in Endpoints.Where(e => which(e.Value)))
        {
            data.Add(method, pattern);
        }

        return data;
    }

    private static class Policies
    {
        public const string Member = nameof(Member);
        public const string Manager = nameof(Manager);
        public const string Owner = nameof(Owner);
    }
}
