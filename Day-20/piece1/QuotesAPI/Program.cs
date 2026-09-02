using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
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
using QuotesApi.BackgroundProcessing;
using QuotesApi.Configuration;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Messaging;
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
// CORS
// ============================================================
//
// The Angular app (Azure Static Web Apps) is served from a different
// origin than this API (Azure Container Apps), so the browser enforces
// CORS on every cross-origin fetch. Origins are read from configuration
// (not a secret — it's a public hostname) rather than hardcoded, and
// scoped to the exact production SWA origin rather than a wildcard.
// ============================================================

var corsAllowedOrigins =
    builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>()
    ?? [];

builder.Services.AddCors(
    options =>
    {
        options.AddPolicy(
            "SwaOrigin",
            policy =>
            {
                policy
                    .WithOrigins(corsAllowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
    });

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
// Background quote processing (queue + BackgroundService)
// ============================================================
//
// POST /api/quotes only enqueues; it never awaits the slow
// formatting/enrichment work itself. QuoteProcessingBackgroundService
// drains the queue continuously and honors the host's shutdown
// CancellationToken. See BackgroundProcessing/ for details.
// ============================================================

builder.Services.Configure<QuoteProcessingOptions>(
    builder.Configuration.GetSection("QuoteProcessing"));

builder.Services.AddSingleton<
    IQuoteProcessingQueue,
    QuoteProcessingQueue>();

builder.Services.AddHostedService<
    QuoteProcessingBackgroundService>();

// ============================================================
// Day 19: Azure Service Bus topic/subscription pub-sub
// ============================================================
//
// POST /api/quotes publishes a QuoteCreated event to a Service Bus topic
// (see QuoteEndpointExtensions), in addition to the Day 18 local queue
// above — the local queue drives this API's own background formatting
// work, the topic fans the same event out to independent subscribers
// (Subscription A / Subscription B), each with their own competing
// consumers. See Messaging/ for details.
//
// No connection string or key is ever configured: authentication is
// DefaultAzureCredential (Azure CLI login locally, managed identity in
// Azure), authorized via an "Azure Service Bus Data Owner" role
// assignment on the namespace. ServiceBus:FullyQualifiedNamespace is a
// hostname, not a secret, so it can live in appsettings.json.
//
// Left unconfigured (as in the Testing environment), Service Bus is
// skipped entirely: a no-op publisher is registered and no subscription
// workers are started, so tests and local runs never need Azure
// connectivity for this feature.
// ============================================================

builder.Services.Configure<ServiceBusOptions>(
    builder.Configuration.GetSection("ServiceBus"));

var serviceBusOptions = builder.Configuration
    .GetSection("ServiceBus")
    .Get<ServiceBusOptions>();

var serviceBusConfigured =
    !string.IsNullOrWhiteSpace(serviceBusOptions?.FullyQualifiedNamespace);

builder.Services.AddScoped<
    IProcessedMessageStore,
    ProcessedMessageStore>();

builder.Services.AddScoped<
    QuoteEventMessageHandler>();

if (serviceBusConfigured)
{
    builder.Services.AddSingleton(_ =>
    {
        // DefaultAzureCredential's full probe chain (workload identity,
        // then managed identity via IMDS, then the rest) is what
        // production wants — Azure Container Apps resolves a managed
        // identity through it with no code change. Locally there is no
        // IMDS endpoint to answer, so probing it costs a real, multi-
        // retry timeout before the chain ever reaches the developer's own
        // `az login` session. Skipping straight to AzureCliCredential in
        // Development avoids that timeout without changing what
        // production authenticates with.
        Azure.Core.TokenCredential credential =
            builder.Environment.IsDevelopment()
                ? new AzureCliCredential()
                : new DefaultAzureCredential();

        return new ServiceBusClient(
            serviceBusOptions!.FullyQualifiedNamespace,
            credential);
    });

    builder.Services.AddSingleton<
        IQuoteEventPublisher,
        ServiceBusQuoteEventPublisher>();

    // Subscription A gets SubscriptionAWorkerCount competing consumers
    // (Worker-A1, Worker-A2, ...); Subscription B gets exactly one. Both
    // subscriptions independently receive every message published to the
    // topic — see ServiceBusSubscriptionWorker for how that differs from
    // the competing-consumers behavior within Subscription A.
    // NOTE: builder.Services.AddHostedService(factory) is deliberately NOT
    // used here. That extension registers via TryAddEnumerable keyed on
    // the factory delegate's return type — since every worker factory
    // below returns the same ServiceBusSubscriptionWorker type, only the
    // first registration would survive and Worker-A2/Worker-B1 would
    // silently never be added. Plain AddSingleton<IHostedService> has no
    // such dedup and adds one independent entry per call, which is what
    // three distinct worker instances actually require.
    for (var i = 1; i <= serviceBusOptions!.SubscriptionAWorkerCount; i++)
    {
        var workerName = $"Worker-A{i}";

        builder.Services.AddSingleton<IHostedService>(sp =>
            ActivatorUtilities.CreateInstance<ServiceBusSubscriptionWorker>(
                sp,
                serviceBusOptions.SubscriptionA,
                workerName));
    }

    builder.Services.AddSingleton<IHostedService>(sp =>
        ActivatorUtilities.CreateInstance<ServiceBusSubscriptionWorker>(
            sp,
            serviceBusOptions.SubscriptionB,
            "Worker-B1"));
}
else
{
    builder.Services.AddSingleton<
        IQuoteEventPublisher,
        NullQuoteEventPublisher>();
}

// ============================================================
// Day 20: Transactional Outbox relay
// ============================================================
//
// POST /api/quotes (see QuoteEndpointExtensions) writes an OutboxMessage
// row in the same EF Core transaction as the quote itself instead of
// publishing to Service Bus inline. OutboxRelayBackgroundService is the
// only thing that later reads unsent rows and publishes them — through the
// same IQuoteEventPublisher registered above (Service Bus or the no-op,
// whichever this environment configured), so it needs no messaging setup
// of its own. Registered unconditionally: it works the same way whether
// publishing actually reaches Service Bus or the no-op publisher.
//
// IOutboxCrashInjector is a test-only seam (see Messaging/
// IOutboxCrashInjector.cs) — production always gets the no-op
// implementation that never interferes with a real publish.
// ============================================================

builder.Services.Configure<OutboxRelayOptions>(
    builder.Configuration.GetSection("OutboxRelay"));

builder.Services.AddSingleton<
    IOutboxCrashInjector,
    NoOpOutboxCrashInjector>();

builder.Services.AddHostedService<
    OutboxRelayBackgroundService>();

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

app.UseCors("SwaOrigin");

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