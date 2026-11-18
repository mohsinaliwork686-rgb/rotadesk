using System.Text.Json.Serialization;

using Rotadesk.Api.Auth;
using Rotadesk.Api.Availability;
using Rotadesk.Api.Demo;
using Rotadesk.Api.Errors;
using Rotadesk.Api.Outbox;
using Rotadesk.Api.Rota;
using Rotadesk.Api.Staff;
using Rotadesk.Infrastructure;
using Rotadesk.Infrastructure.Outbox;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Rotadesk")
    ?? throw new InvalidOperationException("Set ConnectionStrings:Rotadesk to a PostgreSQL connection string.");

builder.Services.AddRotadeskDatabase(connectionString);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, ClaimsTenant>();
builder.Services.AddScoped<CurrentMember>();
builder.Services.AddScoped<Scheduler>();
builder.Services.AddScoped<RotaReader>();
builder.Services.AddScoped<SwapChecker>();
builder.Services.AddScoped<SwapContext>();
builder.Services.AddScoped<Notifications>();
builder.Services.AddScoped<OutboxProcessor>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRotadeskSignIn();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The demo delivers its emails to an inbox page; without it, and without a real provider, they go to the log.
var demo = builder.Configuration.GetValue<bool>("Demo:Enabled");
if (demo)
{
    builder.Services.AddScoped<IEmailSender, DemoInboxSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
}

if (builder.Configuration.GetValue("Outbox:Enabled", true))
{
    builder.Services.AddHostedService<OutboxWorker>();
}

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateRotadeskDatabaseAsync();
}

if (demo)
{
    // The demo data is there to look at; nothing depends on it. If it can't be written, the app says so and serves
    // anyway, because the free host starts it again on every visit after a sleep.
    try
    {
        await DemoSeeder.SeedAsync(app.Services);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Couldn't fill the demo rota. Starting without it.");
    }
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// The React app, built into wwwroot. Its asset names carry a hash of their contents, so browsers can keep them.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = file =>
    {
        if (file.Context.Request.Path.StartsWithSegments("/assets"))
        {
            file.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    },
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api");
api.MapAuthEndpoints();
api.MapStaffEndpoints();
api.MapWeekEndpoints();
api.MapShiftEndpoints();
api.MapSwapEndpoints();
api.MapAvailabilityEndpoints();
if (demo)
{
    api.MapDemoEndpoints();
    api.MapInboxEndpoints();
}

// A mistyped API path gets a 404 the client can read, not the app's HTML.
api.MapFallback(() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "There's no API endpoint here."));

// Every other path without a file extension is a page of the app, which routes in the browser. index.html is
// checked on every load, so a new release shows up straight away.
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = file => file.Context.Response.Headers.CacheControl = "no-cache",
});

app.Run();

/// <summary>Visible to the API tests, which run the app in memory.</summary>
public partial class Program;
