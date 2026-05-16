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

var jwtSecret = builder.Configuration["CHILDDEV_JWT_SECRET"] ?? "dev-secret-min-32-chars-placeholder";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();

app.Run();

public partial class Program { }
