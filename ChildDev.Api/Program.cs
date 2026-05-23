using System.Text;
using ChildDev.Api.Data;
using MudBlazor.Services;
using ChildDev.Api.Endpoints;
using ChildDev.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["CHILDDEV_DB_CONNECTION"]
    ?? "Server=localhost;Database=childdev;User=childdev;Password=dev;";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
        mySqlOptions => mySqlOptions.CommandTimeout(8)));
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
        mySqlOptions => mySqlOptions.CommandTimeout(8)),
    ServiceLifetime.Scoped);

builder.Services.AddSingleton<JwtService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Defer reading CHILDDEV_JWT_SECRET to options-resolution time so tests can inject it
// via WebApplicationFactory.ConfigureAppConfiguration before it is read.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, config) =>
    {
        var secret = config["CHILDDEV_JWT_SECRET"]
            ?? throw new InvalidOperationException("CHILDDEV_JWT_SECRET is not configured");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
        };
    });

builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
builder.Services.AddRequestTimeouts(options =>
    options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(10)
    });

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(
            builder.Configuration["CHILDDEV_CORS_ORIGIN"] ?? "http://localhost:4200",
            "http://localhost:5173",
            "http://localhost:3000")
          .AllowAnyHeader()
          .AllowAnyMethod()));

builder.Services.AddAuthorization();
builder.Services.AddMudServices();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<WebAnalyticsService>();
builder.Services.AddSingleton<WebAuthTokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

var app = builder.Build();

app.UseResponseCompression();
app.UseRequestTimeouts();
var mimeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
mimeProvider.Mappings[".apk"] = "application/vnd.android.package-archive";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = mimeProvider });
app.UseCors();
app.UseRouting();

app.Use(async (ctx, next) =>
{
    var requestId = ctx.Request.Headers["X-Request-ID"].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N")[..12];
    ctx.Response.Headers["X-Request-ID"] = requestId;
    var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("request");
    using (logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId }))
        await next();
});

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/api/health", async (AppDbContext db) =>
{
    try
    {
        await db.Database.CanConnectAsync();
        return Results.Ok(new { status = "ok", utc = DateTime.UtcNow });
    }
    catch
    {
        return Results.Problem("Database unavailable.", statusCode: 503);
    }
});

app.MapRazorComponents<ChildDev.Api.Components.App>().AddInteractiveServerRenderMode();
app.MapAuthEndpoints();
app.MapWebAuthEndpoints();
app.MapJournalEndpoints();
app.MapGoalEndpoints();
app.MapGoalProgressEndpoints();
app.MapTodoEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    db.Database.ExecuteSqlRaw("ALTER TABLE Goals ADD COLUMN IF NOT EXISTS ProgressPercent INT NULL");
    db.Database.ExecuteSqlRaw("ALTER TABLE Goals ADD COLUMN IF NOT EXISTS Category VARCHAR(50) NULL");
    db.Database.ExecuteSqlRaw("ALTER TABLE Goals ADD COLUMN IF NOT EXISTS IsPinned TINYINT(1) NOT NULL DEFAULT 0");
}

app.Run();

public partial class Program { }
