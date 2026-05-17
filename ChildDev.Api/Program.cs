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

builder.Services.AddAuthorization();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

var app = builder.Build();

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
