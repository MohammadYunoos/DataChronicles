using DataChronicles.Api.Hubs;
using DataChronicles.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------------------------------------------------------------------------
// Authentication (Entra ID) — enabled only when configured. Off by default so
// the app runs locally without an Azure AD tenant.
// ---------------------------------------------------------------------------
var authEnabled = config.GetValue<bool>("Auth:Enabled");
if (authEnabled)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(config.GetSection("AzureAd"));
}
builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// CORS for the React dev server (Vite: 5173 / CRA: 3000).
// ---------------------------------------------------------------------------
var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://localhost:3000" };
builder.Services.AddCors(o => o.AddPolicy("AllowFrontend", p =>
    p.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials()));

// ---------------------------------------------------------------------------
// Application Insights — only when an instrumentation key/connection is set.
// ---------------------------------------------------------------------------
var aiConn = config["ApplicationInsights:ConnectionString"];
var aiKey = config["ApplicationInsights:InstrumentationKey"];
if ((!string.IsNullOrWhiteSpace(aiConn) && !aiConn.StartsWith("YOUR_")) ||
    (!string.IsNullOrWhiteSpace(aiKey) && !aiKey.StartsWith("YOUR_")))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

// ---------------------------------------------------------------------------
// EF Core: Azure SQL when a connection string is provided, else InMemory.
// ---------------------------------------------------------------------------
var sqlConn = config.GetConnectionString("Sql") ?? config["ConnectionStrings:Sql"];
builder.Services.AddDbContext<DataChroniclesDbContext>(opt =>
{
    if (!string.IsNullOrWhiteSpace(sqlConn) && !sqlConn.StartsWith("YOUR_"))
        opt.UseSqlServer(sqlConn);
    else
        opt.UseInMemoryDatabase("DataChronicles");
});

// ---------------------------------------------------------------------------
// MVC + SignalR + Swagger + app services.
// ---------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient<ZeroShotClassifierService>(c =>
    c.Timeout = TimeSpan.FromSeconds(120)); // fail fast if HF is unreachable
builder.Services.AddScoped<ExcelInputReader>();
builder.Services.AddScoped<ExcelOutputWriter>();
builder.Services.AddScoped<TicketProcessingService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddSingleton<BlobStorageService>();
builder.Services.AddSingleton<GeneratedFileStore>();

var app = builder.Build();

// Ensure the (InMemory/SQL) schema exists.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DataChroniclesDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");

// Serve the built React SPA from wwwroot (single App Service hosts UI + API).
// In local dev wwwroot is empty and the Vite dev server is used instead.
app.UseDefaultFiles();
app.UseStaticFiles();

if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", time = DateTime.UtcNow }));
app.MapControllers();
app.MapHub<ProgressHub>("/progressHub");

// SPA fallback: any non-API route returns index.html so client-side routing works.
app.MapFallbackToFile("index.html");

app.Run();

// Exposed for integration testing.
public partial class Program { }
