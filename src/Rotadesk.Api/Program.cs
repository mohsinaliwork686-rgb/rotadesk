using System.Text.Json.Serialization;

using Rotadesk.Api.Auth;
using Rotadesk.Api.Availability;
using Rotadesk.Api.Demo;
using Rotadesk.Api.Errors;
using Rotadesk.Api.Rota;
using Rotadesk.Api.Staff;
using Rotadesk.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Rotadesk")
    ?? throw new InvalidOperationException("Set ConnectionStrings:Rotadesk to a PostgreSQL connection string.");

builder.Services.AddRotadeskDatabase(connectionString);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, ClaimsTenant>();
builder.Services.AddScoped<CurrentMember>();
builder.Services.AddScoped<Scheduler>();
builder.Services.AddScoped<RotaReader>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRotadeskSignIn();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateRotadeskDatabaseAsync();
}

var demo = app.Configuration.GetValue<bool>("Demo:Enabled");
if (demo)
{
    await DemoSeeder.SeedAsync(app.Services);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api");
api.MapAuthEndpoints();
api.MapStaffEndpoints();
api.MapWeekEndpoints();
api.MapShiftEndpoints();
api.MapAvailabilityEndpoints();
if (demo)
{
    api.MapDemoEndpoints();
}

app.Run();

/// <summary>Visible to the API tests, which run the app in memory.</summary>
public partial class Program;
