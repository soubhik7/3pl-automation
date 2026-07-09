using Microsoft.EntityFrameworkCore;
using ThreePl.Core.Admin;
using ThreePl.Core.Data;
using ThreePl.Core.Reads;
using ThreePl.Core.Writes;
using ThreePl.Web.Components;
using ThreePl.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Optional gitignored local override for endpoints/SAS + connection string —
// same rule as the old HTML's const API block: secrets never committed.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dbProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var connectionString = builder.Configuration.GetConnectionString("OnboardingDb") ?? "";
builder.Services.AddDbContextFactory<OnboardingDbContext>(options =>
{
    if (string.Equals(dbProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        options.UseSqlite(connectionString);
    else
        options.UseSqlServer(connectionString);
});

builder.Services.Configure<LogicAppOptions>(builder.Configuration.GetSection(LogicAppOptions.SectionName));
builder.Services.AddHttpClient<LogicAppClient>(http => http.Timeout = TimeSpan.FromSeconds(120));

builder.Services.AddScoped<StatusReadService>();
builder.Services.AddScoped<IntakePrefillService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<FieldRequirementService>();
builder.Services.AddScoped<ToastService>();

var app = builder.Build();

// Dev/local fallback only (SQLite): create the schema when it doesn't exist.
// The live Azure SQL schema is owned by schema.sql — never touched from here.
if (app.Configuration.GetValue<bool>("Database:EnsureCreated"))
{
    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OnboardingDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
