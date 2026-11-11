using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
    public HttpClient NewClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task<HttpClient> SignInAsync(TestPerson person)
    {
        var client = NewClient();
        var response = await client.PostJsonAsync("/api/auth/sign-in", new { email = person.Email, password = person.Password });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    public Task<TestBusiness> CreateBusinessAsync(string timeZone = "Europe/London") => TestBusiness.CreateAsync(this, timeZone);
}

public sealed class RotadeskFactory(string connectionString, bool demo) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Rotadesk", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Demo:Enabled", demo ? "true" : "false");
    }
}
