using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TunSociety.Api.Configuration;
using TunSociety.Api.Data;
using TunSociety.Api.Infrastructure;
using TunSociety.Api.Infrastructure.Security;
using TunSociety.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services.Configure<AdminAccountOptions>(
    builder.Configuration.GetSection(AdminAccountOptions.SectionName));

builder.Services.Configure<OllamaOptions>(
    builder.Configuration.GetSection(OllamaOptions.SectionName));

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration is missing.");

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) ||
    jwtOptions.SigningKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey must be configured and at least 32 characters long.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,

            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly",
        policy => policy.RequireRole(RoleNames.Admin));

    options.AddPolicy("ModeratorOrAdmin",
        policy => policy.RequireRole(
            RoleNames.Moderator,
            RoleNames.Admin));

    foreach (var permission in PermissionNames.All)
    {
        options.AddPolicy(permission, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(
                new PermissionRequirement(permission));
        });
    }
});

builder.Services.AddHttpClient<LocalAiService>((serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<
            Microsoft.Extensions.Options.IOptions<OllamaOptions>>()
        .Value;

    client.BaseAddress = new Uri(
        $"{options.BaseUrl.TrimEnd('/')}/");

    client.Timeout = TimeSpan.FromSeconds(
        Math.Max(5, options.TimeoutSeconds));
});

builder.Services.AddScoped<AiScoringClient>();
builder.Services.AddScoped<ModerationService>();
builder.Services.AddScoped<SanctionService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<RolePermissionService>();

builder.Services.AddScoped<
    IAuthorizationHandler,
    PermissionAuthorizationHandler>();

builder.Services.AddSingleton<AvatarStorageService>();
builder.Services.AddSingleton<EventImageStorageService>();
builder.Services.AddSingleton<ProfileMediaStorageService>();

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "DefaultConnection is missing.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        connectionString,
        new MySqlServerVersion(new Version(8, 0, 36))));

var app = builder.Build();

Console.WriteLine("### DEMO LOGIN PATCH ACTIVE ###");

app.Urls.Add(
    "http://0.0.0.0:" +
    Environment.GetEnvironmentVariable("PORT"));

app.Use(async (context, next) =>
{
    context.Response.Headers["Access-Control-Allow-Origin"] = "*";
    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
    context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";

    if (context.Request.Method == "OPTIONS")
    {
        context.Response.StatusCode = 204;
        return;
    }

    await next();
});

app.UseCors("Frontend");

// TEMPORARY DEMO LOGIN BYPASS
app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) &&
        context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
    {
        context.Request.EnableBuffering();

        using var reader = new StreamReader(
            context.Request.Body,
            Encoding.UTF8,
            leaveOpen: true);

        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        try
        {
            var login = JsonSerializer.Deserialize<LoginRequest>(
                body,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (login?.Email == "demo@tunsociety.com" &&
                login.Password == "Demo123!")
            {
                var claims = new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, "demo-user-id"),
                    new Claim(JwtRegisteredClaimNames.Email, "demo@tunsociety.com"),
                    new Claim(ClaimTypes.NameIdentifier, "demo-user-id"),
                    new Claim(ClaimTypes.Email, "demo@tunsociety.com"),
                    new Claim(ClaimTypes.Name, "Demo User"),
                    new Claim(ClaimTypes.Role, RoleNames.Admin)
                };

                var key = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

                var credentials = new SigningCredentials(
                    key,
                    SecurityAlgorithms.HmacSha256);

                var token = new JwtSecurityToken(
                    issuer: jwtOptions.Issuer,
                    audience: jwtOptions.Audience,
                    claims: claims,
                    expires: DateTime.UtcNow.AddDays(7),
                    signingCredentials: credentials);

                var tokenString = new JwtSecurityTokenHandler()
                    .WriteToken(token);

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = 200;

                await context.Response.WriteAsJsonAsync(new
                {
                    token = tokenString,
                    accessToken = tokenString,
                    user = new
                    {
                        id = "demo-user-id",
                        email = "demo@tunsociety.com",
                        username = "Demo User",
                        role = RoleNames.Admin
                    }
                });

                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("DEMO LOGIN BYPASS ERROR:");
            Console.WriteLine(ex.ToString());
        }
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/debug/db", async (ApplicationDbContext db) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync();
        return Results.Ok(new
        {
            database = "connected",
            canConnect
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString());
    }
});

app.MapControllers();

app.Run();

public sealed class LoginRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}