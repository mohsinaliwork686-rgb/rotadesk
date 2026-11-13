using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Rotadesk.Infrastructure;
using Rotadesk.Infrastructure.Outbox;
using Rotadesk.Infrastructure.Persistence;

using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Rotadesk.Api.Tests.ApiFixture))]

namespace Rotadesk.Api.Tests;

/// <summary>
/// The real app in memory, against one real PostgreSQL for the whole run. Starting it applies the migrations and
/// seeds the demo, the same way the live demo starts. Tests keep apart by creating their own businesses.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public RotadeskFactory Factory { get; private set; } = null!;

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        Factory = new RotadeskFactory(ConnectionString, demo: true);
        _ = Factory.Server;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>A client with its own cookies, like a separate browser.</summary>
    public HttpClient NewClient(WebApplicationFactory<Program>? factory = null) =>
        (factory ?? Factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task<HttpClient> SignInAsync(TestPerson person, WebApplicationFactory<Program>? factory = null)
    {
        var client = NewClient(factory);
        var response = await client.PostJsonAsync("/api/auth/sign-in", new { email = person.Email, password = person.Password });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    public Task<TestBusiness> CreateBusinessAsync(string timeZone = "Europe/London") => TestBusiness.CreateAsync(this, timeZone);

    /// <summary>Every email queued for this business, sent or not, oldest first.</summary>
    public async Task<List<OutboxMessage>> OutboxAsync(Guid businessId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<RotadeskDbContext>>();
        await using var db = new RotadeskDbContext(options, new FixedTenant(businessId));
        return await db.OutboxMessages.AsNoTracking().OrderBy(m => m.CreatedAt).ThenBy(m => m.To).ToListAsync(TestContext.Current.CancellationToken);
    }
}

public sealed class RotadeskFactory(string connectionString, bool demo) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Rotadesk", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Demo:Enabled", demo ? "true" : "false");

        // The background worker is off, so a test decides when emails go out; one test turns it on to see it work.
        builder.UseSetting("Outbox:Enabled", "false");

        // The tests schedule in a fixed week, so they need a fixed "now" or they would start failing once it passed.
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new FixedClock(Http.Now)));
    }
}

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
