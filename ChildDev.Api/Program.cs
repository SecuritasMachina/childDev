using System.Text;
using ChildDev.Api.Data;
using ChildDev.Api.Endpoints;
using ChildDev.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["CHILDDEV_DB_CONNECTION"]
    ?? "Server=localhost;Database=childdev;User=childdev;Password=dev;";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

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

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

var app = builder.Build();

app.UseResponseCompression();
app.UseRequestTimeouts();
app.UseCors();

app.Use(async (ctx, next) =>
{
    var requestId = ctx.Request.Headers["X-Request-ID"].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N")[..12];
    ctx.Response.Headers["X-Request-ID"] = requestId;
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

app.MapAuthEndpoints();
app.MapJournalEndpoints();
app.MapGoalEndpoints();
app.MapGoalProgressEndpoints();
app.MapTodoEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.Run();

public partial class Program { }
