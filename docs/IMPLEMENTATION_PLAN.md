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
| `tests/Rotadesk.Infrastructure.Tests` | The database on its own, against real PostgreSQL: migrations, the tenant filters and write guard, `xmin`, round trips (added Tuesday) |
| `tests/Rotadesk.Api.Tests` | The API through `WebApplicationFactory`, against real PostgreSQL in Testcontainers: isolation, roles, concurrency, outbox, migrations |
| `web/` | React and TypeScript with Vite. All logic in `web/src/lib`, no React there |

## 11. Dependencies

| Package | Why |
|---------|-----|
| `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` | The database, migrations, query filters, `xmin` |
| `EFCore.NamingConventions` | snake_case table and column names, so SQL written by hand needs no quotes (added Tuesday) |
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

- [x] Public repo, description and topics
- [x] This plan, committed before the code
- [x] README: the problem first, a screenshot, the live demo link, how to run it, configuration, non-goals
- [x] Every row of the test plan covered, with the tenant isolation suite as its own file
- [x] CI green: format, build with warnings as errors, unit tests, the API tests against real PostgreSQL, the front end checks
- [x] A live demo with two sample businesses and one-click sign-in
- [x] At least 10 commits, one logical change each

## 16. Changes since Monday

**Tuesday 10 November.**

- **The write guard checks instead of stamping.** The plan said it would stamp the tenant on new rows. The domain
  constructors already take the tenant, so the guard refuses a save that adds, changes, deletes or moves another
  business's row instead. The outbox worker uses a separate system context, which the guard allows.
- **Database tests have their own project,** `Rotadesk.Infrastructure.Tests`, so the filters, guard and concurrency are
  proven before the API exists. In CI they run on Linux, because GitHub's macOS and Windows runners can't run Linux
  containers. The domain tests still run on all three.
- **"Until midnight" is 23:59:59.999999,** not `TimeOnly.MaxValue`. PostgreSQL stores times to the microsecond, so the
  .NET maximum came back a fraction earlier, and a round-trip test caught it.


**Wednesday 11 November.**

- **Sign-in accounts have their own context and schema.** Identity's tables sit in `accounts`, in
  `AccountsDbContext`, not in `RotadeskDbContext`. Accounts are looked up before anyone's business is known, so they
  can't be behind the tenant filter, and keeping them out keeps the model test's rule true: every table in the main
  context belongs to a business. The two contexts can't share an EF relationship, so the accounts migration adds the
  foreign key in SQL, on (business, staff member), so an account can't claim one business and point at another's staff
  member. Deleting a staff member deletes their account.
- **Roles are read live.** The cookie holds who you are (account, business, staff member), not your role. Each request
  reads the role from the staff record, once, through the tenant filter. A demoted manager loses manager endpoints on
  their next request, and a cookie whose staff member is gone or belongs to another business passes no policy.
- **Bad input is a 400, a broken rule is a 409.** Times that can never be right (end before start, over 16 hours, a week
  that doesn't start on Monday, "9am") are 400s. A rule that refuses the change now (an overlap, the weekly limit, the
  last owner, an already published week) is a 409 that names the rule. A stale save is a 409 too.
- **Copying last week is in.** The non-goals mention it, so it's an endpoint: same local times, so 09:00 stays 09:00
  across a clock change, only into an empty, unpublished week, and a shift its person can't take any more is copied
  without them with the reason.
- **A shift stays in its week,** and a shift that's part of a swap request can't be deleted. Availability is replaced as a
  whole and merged where windows overlap or touch, and "24:00" means until midnight.
- **The demo seed has people today.** Demo sign-in needs someone to sign in as, so the two businesses and their staff
  (no passwords) are seeded now. Their shifts, availability and swaps come with the rest of the seed data on Tuesday.
- **Found, for Thursday's isolation work:** with the tenant filter bypassed on purpose, the database accepted a shift of
  one business assigned to another business's staff member, because that foreign key is on the staff member's id alone.
  The filter stops it today; composite (business, id) keys on shifts, availability and swap requests would make the
  database refuse it as well.
- **For deploy day:** the cookie is `HttpOnly` and `SameSite=Lax`. Behind the host's HTTPS proxy it needs the forwarded
  headers set, so the app sees HTTPS and marks the cookie `Secure`.

**Thursday 12 November.**

- **The database refuses links between businesses.** Every foreign key between two business-owned rows is now on
  (business, id): shifts to their week and staff member, availability to its staff member, swaps to both people and
  both shifts. Staff members, weeks and shifts carry an alternate key on (business, id) for this. A model test makes
  it a rule for any new table, and database tests save cross-business rows through the system context, which skips
  the filters and the write guard, and get a foreign key violation. Wednesday's unique index on staff members is
  turned into the alternate key in place (`UNIQUE USING INDEX`), because the accounts' foreign key already depends on
  it; a migration test upgrades a database that has accounts. One more effect: EF now treats the business as part of
  those rows' keys, so it refuses to move one to another business before the write guard even sees it.
- **Swaps go through the state machine,** and the rules are checked when a swap is asked for, so a swap that can't
  work is refused straight away, and again at approval. Only shifts on a published rota can be swapped, and a shift
  can be in one open swap at a time. Staff see the swaps they're part of; anyone else's is a 404. The right request
  from the wrong person (accepting someone else's swap, deciding your own) is a 403. The domain gained
  `EnsureCanApprove`, so the API answers "not yet accepted" or "your own swap" before it checks the rules.
- **The concurrency test is deterministic.** A test-only interceptor changes the swap's row on another connection just
  before an approval saves, as a second manager would. The approval's `xmin` no longer matches, and the answer is a
  409 with nothing moved. Ten simultaneous approvals are tested too: exactly one wins.
- **Every endpoint is in one table** in the API tests: who may call it, and what kind of id it takes. A test checks
  the table against the app's endpoints, so a new endpoint can't skip the 401, 403 and cross-business 404 checks.
- **The API tests use a fixed clock,** 10 November 09:00 UTC like the domain tests, so the swap tests don't start
  failing once the test week is in the past.

**Friday 13 November.**

- **The outbox claims a message before sending it.** Claiming counts the attempt and sets a five-minute lease, and
  `xmin` on the row means two workers that read the same message can't both claim it. A worker that stops mid-send
  leaves the lease to run out, and the message goes again: at least once, never lost. `FOR UPDATE SKIP LOCKED` would
  scale better with many workers; that, and exactly-once delivery, are `hookline`'s next week.
- **Backoff is 30 seconds, then 1, 2 and 4 minutes,** and the fifth failure gives up, keeping the error.
- **The demo inbox is a table,** written by the demo's sender through the email's own business, and each person sees
  their own at `/api/inbox` (demo only). It's a delivery log, not a view of the outbox, so a message sent twice after
  a crash shows twice with the same message id. Without the demo, emails go to the log.
- **Which changes email whom:** a published week (each person with a shift, with their shifts), a swap asked for (the
  colleague), accepted (the managers who aren't part of it), approved or rejected (both people), and a shift added,
  changed or removed in a published week (whoever had it and whoever has it now). Changes to an unpublished week email
  nobody.
- **"Only the outbox crosses businesses" is a test** that reads the source: nothing calls `IgnoreQueryFilters`, and the
  system context appears only in the outbox processor, the migrations and the design-time factory. The worker reads
  through the system context rather than `IgnoreQueryFilters`, as Tuesday's notes say.
- **Tests decide when emails go out.** The test app runs with the worker off, and one test turns it on to see a rota
  arrive in the demo inbox.

**Monday 16 November.**

- **The front end is React and TypeScript, built with Vite, in `web/`.** All the logic lives in `web/src/lib` with
  no React in it: the API client, the time helpers, the rota and the availability helpers, each with Vitest tests.
  The pages call those and draw. Page tests render the real app in jsdom and fake only `fetch`.
- **Times are on the business's clock, never the browser's.** A manager in Karachi editing a London café's rota
  sees and types London times. The helpers convert through `Intl` with the business's zone, and the tests run with
  the machine set to Auckland, so a helper that used the browser's zone fails. When the clocks go back, a time that
  happens twice means the first; a time the clocks skip moves forward half an hour at a time, the rule the API uses
  when it copies a week.
- **An end at or before the start is the next morning,** so 22:00 to 06:00 is a night shift and the API gets two UTC
  instants eight hours apart (nine over the October change).
- **The API serves the built app from the same origin,** so the cookie needs no CORS. Vite builds into the API's
  `wwwroot` (ignored by git), unknown `/api` paths get a ProblemDetails 404 rather than the page, and any other path
  without a file extension gets `index.html`, never cached; the hashed assets are cached for a year. In development
  the Vite server forwards `/api` to the API.
- **A 401 from any call shows the sign-in page,** and the person comes back to the page they asked for, unless
  someone signed out on purpose, when the next person starts on the rota.
- **CI has a web job:** lint, tests, then the type-checked build.

**Tuesday 17 November.**

- **The demo keeps its rota current.** Render's free tier sleeps after 15 idle minutes and starts the app again on
  the next visit, so the seeder runs at every start. Each time, it makes sure both businesses have this week (in
  their own time zone) published and next week as a draft, filled from a typical week. A week that already has
  shifts is left alone, so what visitors change stays until the week is over. The first start also adds weekly
  limits and availability.
- **The seed shows the features without breaking a rule.** Amira isn't free before 08:00 on Fridays, so one shift
  carries a warning. Each week has one open shift. Northside has an accepted swap waiting for a manager, and Harbour
  Books a request waiting for Hira. A test checks every seeded shift against the scheduling rules, and another
  approves both swaps, which checks the rules again for both people.
- **Swaps in the front end.** Staff click one of their own shifts on a published rota to offer it to a colleague,
  maybe for one of theirs; a shift that has started isn't offered, matching the API. The swaps page lists what's
  waiting and what's finished, and shows only the buttons the state machine allows this person: the colleague
  answers, the person who asked can cancel, and a manager who isn't part of it decides. That logic lives in
  `web/src/lib/swaps.ts`, with tests.
- **The demo inbox in the front end.** The link shows only on the demo. Emails arrive a few seconds after the change,
  through the outbox worker.
- **The swap decision email speaks to each person about their own shift** ("your shift", "for yours"), where it used
  to name the requester to both.
- **Docker.** One image: Node builds the front end, the .NET SDK publishes the API with it in `wwwroot`, and the
  ASP.NET runtime image runs it as a non-root user on port 8080. `compose.yaml` adds PostgreSQL and maps the app to
  `localhost:5180` (8080 is often taken locally). On the very first start EF logs two failed reads of the migrations
  history table before it creates it; they don't come back on later starts.

**Wednesday 18 November: deploy day.**

- **Live at https://rotadesk.onrender.com**, as planned: one Docker web service on Render's free plan in Frankfurt,
  with Neon's free PostgreSQL in the same region. Render now asks for a card even for the free plan; Neon doesn't.
  The service was created in the dashboard with the settings in `render.yaml`, which stays as the record of them.
  Render deploys a commit only after its CI checks pass.
- **Found before it reached the host: the demo crashed on a late first start.** The seed builds this week's sample
  swaps from fixed days, and a swap whose shift has begun is refused, so the first start of a week after Usman's
  Wednesday threw and the app never came up. On the free host, which starts the app at every visit after a sleep, the
  demo would have stayed down until the next Monday. The fixture's clock is a Tuesday morning, so the tests never saw
  it. Now a sample swap whose shifts have begun is left out, and if the demo data still can't be written, the app logs
  it and serves anyway. Two tests start the app on a Thursday and on a Sunday.
- **Behind the host's proxy.** `ASPNETCORE_FORWARDEDHEADERS_ENABLED` makes the app trust `X-Forwarded-Proto`, so it
  sees HTTPS and marks the cookie `Secure`; checked locally with the header and again on the live site. `PORT=8080`
  points Render at the port the image listens on.
- **Quieter logs.** Npgsql no longer tries GSS encryption, which logged a Kerberos error at every start in the slim
  image, and EF's per-command logging is off in production.
- **One deploy failed** on a connection string pasted with the quotes from Neon's snippet around it. The README says
  the value is the bare `Host=...` form.
- **Left as it is:** the data protection keys live in the container, so a sleep signs visitors out. The demo's
  one-click sign-in makes that cheap; keeping the keys in the database would be the fix.
