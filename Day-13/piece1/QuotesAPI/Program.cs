using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using QuotesApi.Authorization;
using QuotesApi.Configuration;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Models;
using QuotesApi.Options;
using QuotesApi.Repositories;
using QuotesApi.Services;
using Serilog;
using Serilog.Context;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// Custom ActivitySource
// ============================================================

var activitySource = new ActivitySource("QuotesApi");

// ============================================================
// Polly / HTTP Client Resilience
// ============================================================

builder.Services
    .AddHttpClient("my-service", client =>
    {
        // Intentionally unavailable endpoint for resilience testing.
        // This forces transient failures and demonstrates retries.
        client.BaseAddress = new Uri("https://localhost:59999");
    })
    .AddResilienceHandler("default", resilienceBuilder =>
    {
        // Retry: 3 attempts with exponential backoff + jitter
        resilienceBuilder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,

            OnRetry = args =>
            {
                Console.WriteLine(
                    $"RETRY: Attempt {args.AttemptNumber + 1}, " +
                    $"Delay: {args.RetryDelay.TotalMilliseconds}ms");

                return default;
            }
        });

        // Circuit breaker
        resilienceBuilder.AddCircuitBreaker(
            new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 2,
                BreakDuration = TimeSpan.FromSeconds(10)
            });

        // Overall timeout
        resilienceBuilder.AddTimeout(
            TimeSpan.FromSeconds(10));
    });

// ============================================================
// JWT Options
// ============================================================

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection("Jwt"));

// ============================================================
// Azure Key Vault (production secret source)
// ============================================================
//
// The Application Insights connection string is NEVER
// hardcoded or stored in appsettings.
//
// In production, Key Vault is used when KeyVault:VaultUri
// is configured.
//
// Locally, KeyVault:VaultUri can remain empty and the
// application uses User Secrets / environment variables.
// ============================================================

builder.Services.Configure<KeyVaultOptions>(
    builder.Configuration.GetSection("KeyVault"));

var keyVaultOptions = builder.Configuration
    .GetSection("KeyVault")
    .Get<KeyVaultOptions>();

if (!string.IsNullOrWhiteSpace(keyVaultOptions?.VaultUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultOptions.VaultUri),
        new DefaultAzureCredential());
}

// ============================================================
// Application Insights configuration
// ============================================================

var applicationInsightsConnectionString =
    builder.Configuration["ApplicationInsightsConnectionString"]
    ?? builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

var useAzureMonitor =
    !string.IsNullOrWhiteSpace(
        applicationInsightsConnectionString);

// ============================================================
// Serilog
// ============================================================

builder.Logging.ClearProviders();

builder.Host.UseSerilog(
    (context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(
                context.Configuration)

            .Enrich.FromLogContext()

            // ------------------------------------------------
            // Console
            // ------------------------------------------------

            .WriteTo.Console(
                outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] " +
                    "[TraceId:{TraceId}] " +
                    "[SpanId:{SpanId}] " +
                    "{Message:lj}{NewLine}{Exception}")

            // ------------------------------------------------
            // Aspire Dashboard / OpenTelemetry
            // ------------------------------------------------

            .WriteTo.OpenTelemetry(
                options =>
                {
                    options.Endpoint =
                        "http://localhost:4317";

                    options.ResourceAttributes =
                        new Dictionary<string, object>
                        {
                            ["service.name"] =
                                "QuotesApi"
                        };
                });
    },

    // Forward Serilog events to registered
    // ILogger providers such as Azure Monitor.
    writeToProviders: true);

// ============================================================
// OpenTelemetry
// ============================================================

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(
        resource =>
            resource.AddService("QuotesApi"))

    .WithTracing(
        tracing =>
        {
            tracing

                // Custom application spans
                .AddSource("QuotesApi")

                // EF Core instrumentation
                .AddEntityFrameworkCoreInstrumentation()

                // Aspire / OTLP
                .AddOtlpExporter(
                    options =>
                    {
                        options.Endpoint =
                            new Uri(
                                "http://localhost:4317");
                    });

            if (!useAzureMonitor)
            {
                // When Azure Monitor is not configured,
                // use normal local instrumentation.

                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            }
        });

// ============================================================
// Azure Monitor
// ============================================================

if (useAzureMonitor)
{
    // Current Microsoft-supported Azure Monitor
    // OpenTelemetry package/API:
    //
    // Azure.Monitor.OpenTelemetry.AspNetCore
    // + UseAzureMonitor()

    builder.Services
        .AddOpenTelemetry()
        .UseAzureMonitor(
            options =>
            {
                options.ConnectionString =
                    applicationInsightsConnectionString;
            });
}

// ============================================================
// Configuration
// ============================================================

var jwtOptions = builder.Configuration
    .GetSection("Jwt")
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException(
        "JWT configuration is not configured.");

if (string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    throw new InvalidOperationException(
        "JWT signing key is not configured.");
}

if (string.IsNullOrWhiteSpace(jwtOptions.Issuer))
{
    throw new InvalidOperationException(
        "JWT issuer is not configured.");
}

if (string.IsNullOrWhiteSpace(jwtOptions.Audience))
{
    throw new InvalidOperationException(
        "JWT audience is not configured.");
}

var entraTenantId =
    builder.Configuration["Entra:TenantId"]
    ?? throw new InvalidOperationException(
        "Entra tenant ID is not configured.");

var entraAudience =
    builder.Configuration["Entra:Audience"]
    ?? throw new InvalidOperationException(
        "Entra audience is not configured.");

// ============================================================
// Authentication
// ============================================================

builder.Services

    .AddAuthentication(
        options =>
        {
            options.DefaultAuthenticateScheme =
                "Smart";

            options.DefaultChallengeScheme =
                "Smart";
        })

    // ========================================================
    // Internal JWT
    // ========================================================

    .AddJwtBearer(
        "InternalJwt",
        options =>
        {
            options.TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    ValidIssuer =
                        jwtOptions.Issuer,

                    ValidAudience =
                        jwtOptions.Audience,

                    IssuerSigningKey =
                        new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(
                                jwtOptions.Key))
                };
        })

    // ========================================================
    // Microsoft Entra JWT
    // ========================================================

    .AddJwtBearer(
        "EntraJwt",
        options =>
        {
            options.Authority =
                $"https://login.microsoftonline.com/" +
                $"{entraTenantId}/v2.0";

            options.TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,

                    ValidAudience =
                        entraAudience
                };
        })

    // ========================================================
    // Smart Policy Scheme
    // ========================================================

    .AddPolicyScheme(
        "Smart",
        "Internal JWT or Microsoft Entra JWT",
        options =>
        {
            options.ForwardDefaultSelector =
                context =>
                {
                    var authorization =
                        context.Request
                            .Headers
                            .Authorization
                            .ToString();

                    // No Bearer token
                    if (!authorization.StartsWith(
                            "Bearer ",
                            StringComparison
                                .OrdinalIgnoreCase))
                    {
                        return "InternalJwt";
                    }

                    var token =
                        authorization[
                            "Bearer ".Length..]
                        .Trim();

                    try
                    {
                        var jwt =
                            new JwtSecurityTokenHandler()
                                .ReadJwtToken(token);

                        // Microsoft Entra issuer
                        if (jwt.Issuer.Contains(
                                "login.microsoftonline.com",
                                StringComparison
                                    .OrdinalIgnoreCase))
                        {
                            return "EntraJwt";
                        }

                        // Internal JWT
                        return "InternalJwt";
                    }
                    catch
                    {
                        return "InternalJwt";
                    }
                };
        });

// ============================================================
// Authorization
// ============================================================

builder.Services.AddAuthorization(
    options =>
    {
        options.AddPolicy(
            "can-edit-quotes",
            policy =>
            {
                policy.RequireClaim(
                    "scope",
                    "quotes.write");

                policy.AddRequirements(
                    new CanDeleteQuoteRequirement());
            });
    });

// ============================================================
// Database
// ============================================================

builder.Services.AddInfrastructure(
    builder.Configuration);

// ============================================================
// Repositories
// ============================================================

builder.Services.AddScoped<
    IQuoteRepository,
    QuoteRepository>();

builder.Services.AddScoped<
    ICollectionRepository,
    CollectionRepository>();

// ============================================================
// Authorization Handlers
// ============================================================

builder.Services.AddScoped<
    IAuthorizationHandler,
    CanDeleteQuoteHandler>();

// ============================================================
// DI lifetime exercise
// ============================================================

builder.Services.AddSingleton<
    IClock,
    QuotesApi.Services.SystemClock>();

builder.Services.AddTransient<
    QuoteFormatter>();

builder.Services.AddTransient<
    RefreshTokenManager>();

// ============================================================
// Build application
// ============================================================

var app = builder.Build();

// ============================================================
// Request TraceId / SpanId correlation
// ============================================================

app.Use(
    async (context, next) =>
    {
        var activity =
            Activity.Current;

        using (LogContext.PushProperty(
            "TraceId",
            activity?.TraceId.ToString()
                ?? "none"))

        using (LogContext.PushProperty(
            "SpanId",
            activity?.SpanId.ToString()
                ?? "none"))
        {
            // ------------------------------------------------
            // Custom application span
            // ------------------------------------------------

            using var customActivity =
                activitySource.StartActivity(
                    "application-processing");

            customActivity?.SetTag(
                "application.component",
                "QuotesApi");

            customActivity?.SetTag(
                "http.method",
                context.Request.Method);

            customActivity?.SetTag(
                "http.path",
                context.Request.Path.ToString());

            await next();
        }
    });

// ============================================================
// Middleware
// ============================================================

app.UseAuthentication();

app.UseAuthorization();

// ============================================================
// Create / update database
// ============================================================

using (var scope =
       app.Services.CreateScope())
{
    var db =
        scope.ServiceProvider
            .GetRequiredService<
                QuotesDbContext>();

    db.Database.Migrate();

    if (!db.Users.Any())
    {
        db.Users.Add(
            new User
            {
                Email =
                    "test@example.com",

                PasswordHash =
                    BCrypt.Net.BCrypt.HashPassword(
                        "Password123!")
            });

        db.SaveChanges();
    }
}

// ============================================================
// API endpoints
// ============================================================

app.MapAuthEndpoints();

app.MapQuoteEndpoints();

app.MapCollectionEndpoints();

// ============================================================
// Health endpoint
// ============================================================

app.MapGet("/health", () =>
    Results.Ok(new { status = "Healthy" }));

// ============================================================
// Resilience test endpoint
// ============================================================

if (app.Environment.IsDevelopment())
{
    app.MapGet(
        "/test-resilience",
        async (IHttpClientFactory factory) =>
        {
            var client =
                factory.CreateClient("my-service");

            try
            {
                var response =
                    await client.GetAsync("/test");

                return Results.Ok(
                    new
                    {
                        status =
                            (int)response.StatusCode
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"FINAL FAILURE: {ex.Message}");

                return Results.Problem(
                    "External service failed after resilience attempts.");
            }
        });
}

// ============================================================
// Run
// ============================================================

app.Run();

// ============================================================
// Required for integration tests
// ============================================================

public partial class Program
{
}