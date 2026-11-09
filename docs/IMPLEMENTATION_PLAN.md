# Implementation plan — rotadesk

> Written Monday 9 November 2026, before any code. Weeks 8 and 9 of the programme, seven and a half
> days, shipping Wednesday 18 November at midday.

## 1. Problem

A café or a small shop plans its staff week by week, usually in a spreadsheet or a group chat. The
manager builds the rota, sends a screenshot, and then spends the week handling "can someone take my
Saturday?" messages. Nobody can see who has agreed to what, double bookings slip through, and
someone ends up working 50 hours because a swap was never checked.

`rotadesk` is staff scheduling for businesses like that:

- A manager builds the week's rota and publishes it.
- Staff say when they can work, see their shifts, and ask a colleague to swap one.
- The colleague accepts, a manager approves, and everyone affected gets an email.
- Each business sees only its own staff and shifts.

It is built the way a software house builds a client system: one app serving many businesses, with
sign-in and roles, real business rules enforced in one place, safe background email, a small web
front end, and a live demo a recruiter can click around without signing up.

## 2. Non-goals

- **No payroll, clocking in, billing or mobile app.**
- **No real email provider.** Emails go through an `IEmailSender`. The demo's sender writes them to
  an inbox page, so nobody receives mail from a demo. A real provider would be one new class.
- **No sign-up.** Businesses and users are created by seed data and by an Owner inviting staff.
  Self-service sign-up, password reset by email and two-factor sign-in are left out.
- **One business per user.** Someone working at two cafés would need two accounts.
- **No recurring shift templates.** A manager can copy last week instead.
- **No real-time updates.** Pages refresh their data when you act or reload.
- **Nothing from work**: no code, no patterns copied, no healthcare domain.

## 3. What people do

**A manager** signs in and opens the week. They add shifts, give each one to a staff member, and see
straight away if that person is already working, would go over their weekly hours, or said they
aren't available. When the week looks right, they publish it, and each person with a shift gets an
email. They approve or reject swap requests that a colleague has accepted.

**Staff** sign in and see their shifts for the week. They set when they can usually work, for
example "Mondays 09:00 to 17:00". To swap, they pick one of their shifts and a colleague, and
optionally a shift of the colleague's to take in return. The colleague accepts or declines.

**The Owner** does everything a manager does, and also adds staff and sets each person's role and
weekly hours limit.

**A recruiter** opens the demo, clicks "Sign in as a manager at Northside Café", and is in.

The API is plain JSON under `/api`. For example:

```http
POST /api/shifts
{ "start": "2026-11-16T09:00:00Z", "end": "2026-11-16T17:00:00Z", "staffMemberId": "..." }

201 Created
{ "id": "...", "start": "...", "end": "...", "staffMemberId": "...",
  "warnings": ["Outside Amira's availability on Monday"] }
```

A broken rule is a `409 Conflict` with a ProblemDetails body that names the rule, and a stale write
is a `409` that says someone else changed it first.

## 4. The four decisions the roadmap left for Monday

### 4a. Tenant filter, or row-level security as well

**The EF Core filter, a write guard, and tests that prove both. No row-level security for now.**

- Every tenant-owned entity gets a global query filter, set once on the model, so a forgotten `WHERE`
  can't happen.
- A `SaveChanges` interceptor stamps the tenant on new rows and refuses to change or delete a row of
  another tenant. That closes the gap filters leave: they cover reads, not writes.
- A model test fails if any tenant-owned entity has no filter, so a new table can't quietly leak.
- The isolation suite tries every endpoint as a user of the other business, including guessed ids.

PostgreSQL row-level security would be a second wall, but it needs the tenant set on every pooled
connection before each command. That is a second, separate piece of tenant plumbing to get right and
test, and the week is better spent on the suite that proves the first wall. If this were real client
work holding personal data, I would add it next. The README says so.

### 4b. Controllers or minimal APIs

**Minimal APIs, in route groups.** They are the default in the .NET 10 templates, need less
ceremony, and keep each endpoint next to its policy: `MapGroup("/api/swaps").RequireAuthorization()`.
The endpoints stay thin: they read the request, call the domain, and map the result.

### 4c. A shift outside someone's availability

**Allowed, with a warning.** Availability is what someone usually prefers, and the manager knows
when that's flexible. Overlapping shifts and the weekly hours limit are hard rules and are refused.
The warning comes back with the shift and shows on the rota.

### 4d. Where the live demo runs

Checked today. Render's free tier runs Docker web services but sleeps them after 15 minutes idle
(about a minute to wake), and its free PostgreSQL expires after 30 days. Neon's free PostgreSQL has
1 GB and 100 compute hours a month and scales to zero. Azure App Service F1 is free but stops an app
after 60 CPU minutes a day.

**The plan: the API and front end as one Docker service on Render's free tier, with Neon's free
PostgreSQL.** The front end is built into the API's `wwwroot`, so it's one origin and sign-in is a
plain cookie. The README says the first request may take a minute while it wakes. **Fallback:** Azure
App Service F1 with the same Neon database. Confirmed on deploy day, Wednesday 18 November.

## 5. The domain and its rules

All of this lives in `Rotadesk.Domain`, which is pure C# with no database or web references.

| Entity | What it holds |
|--------|---------------|
| `Business` | The tenant: name and time zone (an IANA id such as `Europe/London`) |
| `StaffMember` | Name, email, role (Owner, Manager or Staff), weekly hours limit |
| `RotaWeek` | A week of one business, starting on a Monday, and whether it's published |
| `Shift` | Start and end (stored in UTC), and who it's assigned to, if anyone |
| `Availability` | A weekly window when someone can work: a day, a start time and an end time, in the business's time |
| `SwapRequest` | The offered shift, the colleague, an optional shift in return, and the state |

**The rules:**

- **No overlapping shifts** for one person. A shift ending at 17:00 and one starting at 17:00 don't
  overlap.
- **A weekly hours limit** per person. A shift that crosses from Sunday night into Monday counts the
  hours on each side towards its own week. Weeks are Monday to Sunday in the business's time zone.
- **Availability** gives a warning, never a refusal (4c).
- **A shift lasts more than zero minutes and at most 16 hours**, and ends after it starts.
- **A published week changes only through a swap or a manager's edit.** A manager's edit to a
  published shift emails whoever is affected.

**The swap state machine.** The brief's states, plus `Declined`, so a colleague can say no:

```
Requested ──accept──▶ Accepted ──approve──▶ Approved
    │                    │
    ├──decline─▶ Declined └──reject───▶ Rejected
    └──cancel──▶ Cancelled ◀──cancel── (Accepted)
```

Only the colleague accepts or declines, only the requester cancels, and a manager approves or
rejects only after the colleague has accepted. Every other move throws. Approving re-checks the
rules for both people as the shifts will be after the swap, because the rota may have changed since
the request, and then moves the shifts.

## 6. Tenancy

- The tenant always comes from the signed-in user's `tenant_id` claim, never from the request.
- `ITenantContext` reads it once per request. The database context applies it to every filter.
- Another business's id returns `404`, not `403`, so ids can't be probed for existence.
- The outbox worker runs outside a request, across all businesses. It is the only code that calls
  `IgnoreQueryFilters()`, and a test checks that.

## 7. Sign-in and roles

ASP.NET Core Identity, with its EF Core stores in the same PostgreSQL database and a cookie, because
the front end is on the same origin. Policies: `Member` (any signed-in member of the business),
`Manager` (Manager or Owner) and `Owner`. The demo has an endpoint that signs you in as a seeded
person with one click. It is switched on only when `Demo:Enabled` is set.

## 8. Concurrency

`SwapRequest` and `Shift` carry PostgreSQL's `xmin` as a concurrency token. If two managers approve
the same swap at once, the second save fails, and the API returns `409` with "someone else changed
this first", instead of overwriting.

## 9. The outbox and the demo inbox

Each change that should send an email writes an `OutboxMessage` row in the same transaction. A
`BackgroundService` picks up unsent rows, sends them through `IEmailSender`, and marks them sent. A
failed send is retried with backoff and gives up after five attempts, keeping the last error. A crash
between saving and sending loses nothing, because the row is still there. Sending twice after a
crash in the middle is possible, so the demo inbox shows the message id. Exactly-once delivery is
`hookline`'s job, next week.

Emails sent: a rota is published (each person with a shift), a swap is requested (the colleague), a
swap is accepted (the managers), a swap is approved or rejected (both people), and a published shift
is edited (whoever is affected).

## 10. Projects

| Project | Job |
|---------|-----|
| `src/Rotadesk.Domain` | Entities, the rules and the swap state machine. No references outside the base library |
| `src/Rotadesk.Infrastructure` | `RotadeskDbContext`, mappings, the tenant filter and write guard, migrations, the outbox store, the email senders |
| `src/Rotadesk.Api` | Startup, endpoints, policies, ProblemDetails, the outbox worker, demo seed data, serving the front end |
| `tests/Rotadesk.Domain.Tests` | Fast unit tests of the rules and the state machine |
| `tests/Rotadesk.Api.Tests` | The API through `WebApplicationFactory`, against real PostgreSQL in Testcontainers: isolation, roles, concurrency, outbox, migrations |
| `web/` | React and TypeScript with Vite. All logic in `web/src/lib`, no React there |

## 11. Dependencies

| Package | Why |
|---------|-----|
| `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` | The database, migrations, query filters, `xmin` |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | Users, password hashes and roles, without writing my own |
| `xunit.v3` | Tests, on Microsoft.Testing.Platform (changed Monday: the current xUnit v3 needs it, and it needs no other test packages) |
| `Testcontainers.PostgreSql` | Real PostgreSQL in tests: constraints and concurrency behave as in production |
| `Microsoft.AspNetCore.Mvc.Testing` | Runs the API in memory for the HTTP tests |
| `react`, `react-router`, `vite`, `typescript`, `vitest` | The front end and its tests |

Left out on purpose: MediatR, AutoMapper, FluentValidation and Serilog. Each is one more thing to
learn and configure, and the built-in pieces cover what this app needs.

## 12. Build order

| Day | Work |
|-----|------|
| **Mon 9** | This plan, committed first. The solution skeleton. The domain: shifts, the overlap and hours rules, availability warnings, the swap state machine, all unit tested. CI building and running them |
| **Tue 10** | EF Core and PostgreSQL, the mappings, the first migration, the tenant filter and write guard, the model test, the Testcontainers fixture, migrations applying to an empty database |
| **Wed 11** | Identity, roles and policies, demo sign-in, the tenant from claims. Endpoints for staff, rota weeks, shifts and availability |
| **Thu 12** | Swap endpoints through the state machine, `xmin` concurrency, the tenant isolation suite over every endpoint, the role tests |
| **Fri 13** | The outbox and its worker, retries, the crash test, the demo inbox. CI with the full suite |
| **Mon 16** | The front end: sign-in, the rota week, availability |
| **Tue 17** | The front end: swaps and the inbox. Seed data for two businesses. Dockerfile and compose |
| **Wed 18 AM** | Deploy, README with screenshots, publish, portfolio, roadmap and CV |

## 13. Test plan

| Area | Must work | Must not happen |
|------|-----------|-----------------|
| Overlap | shifts that overlap by a minute are refused | back-to-back shifts (17:00 to 17:00) refused; a shift clashing with itself when edited |
| Hours limit | going over by one minute is refused; exactly at the limit is allowed | a shift across Sunday midnight counted wholly in one week |
| Availability | a shift outside the window comes back with a warning | a warning for a shift inside the window; a refusal instead of a warning |
| Shift times | end before start, zero length and over 16 hours are refused | a valid 16-hour shift refused |
| State machine | every legal move works | any illegal move (approve before accept, accept by someone else, anything after a final state) |
| Swap approval | approving moves both shifts and re-checks the rules | approving a swap that now breaks a rule |
| Tenancy | each business sees its own data | reading or changing another business's data through any endpoint, including a guessed id (expect `404`); a tenant-owned entity with no filter |
| Roles | managers reach manager endpoints | Staff reaching manager endpoints; Managers reaching Owner endpoints |
| Concurrency | one of two simultaneous approvals wins | both succeeding; the loser getting a `500` instead of a `409` |
| Outbox | a queued email is sent and marked sent; a failed send is retried | an email lost when the worker crashes before sending; retrying forever |
| Database | migrations apply to an empty PostgreSQL | tests passing only against a fake |
| Front end | the date and rota helpers in `web/src/lib` | logic inside components |

## 14. Risks

| Risk | Mitigation |
|------|------------|
| Seven and a half days for a full-stack app | The backend comes first and is complete by Friday. The front end covers the flows the demo needs, nothing more |
| Time zones and weeks get subtle | Weeks and availability are computed in the business's time zone with tests at the edges. Shifts are stored in UTC |
| Docker on this machine also starts an unrelated cluster | Tests only use their own Testcontainers PostgreSQL. CI runs them on GitHub's Linux runners |
| Render's free tier sleeps and may need a card | The README warns about waking. Azure F1 is the fallback |
| A tenant leak would undo the point of the project | The filter, the write guard, the model test and the isolation suite, all four |

## 15. Definition of done

- [ ] Public repo, description and topics
- [ ] This plan, committed before the code
- [ ] README: the problem first, a screenshot, the live demo link, how to run it, configuration, non-goals
- [ ] Every row of the test plan covered, with the tenant isolation suite as its own file
- [ ] CI green: format, build with warnings as errors, unit tests, the API tests against real PostgreSQL, the front end checks
- [ ] A live demo with two sample businesses and one-click sign-in
- [ ] At least 10 commits, one logical change each
