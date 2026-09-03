# Azure Monitor / production telemetry

This document covers the production telemetry path added on top of the
existing local Aspire/OpenTelemetry setup: Application Insights, Key
Vault, the OpenTelemetry wiring, the KQL queries, and the response-time
alert.

**Honesty note up front:** the environment this was built in does not
have the Azure CLI installed, so no Azure resource was actually created
and no alert was actually verified against live data. Everything below
that is code/configuration in this repo has been built, built-tested,
and run locally. Everything that requires an Azure subscription is
called out explicitly as **NOT COMPLETED — manual action required**,
with the exact commands to finish it.

## Package/API correction

The task described `Microsoft.Azure.Monitor.OpenTelemetry.AspNetCore` +
`AddAzureMonitor()`. That naming is obsolete. The current
Microsoft-supported package for ASP.NET Core is:

```
Azure.Monitor.OpenTelemetry.AspNetCore  (confirmed 1.6.0 on NuGet.org, net10.0-compatible)
```

registered via `builder.Services.AddOpenTelemetry().UseAzureMonitor(...)`.
This is what's implemented in `Program.cs`.

## Azure resources (target state)

| Resource | Suggested name | Purpose | Status |
|---|---|---|---|
| Resource group | `ThinkSchool-Day4` | Container for all of the below | NOT COMPLETED — manual action required |
| Log Analytics workspace | `quotes-api-workspace` | Backing store for the workspace-based Application Insights resource | NOT COMPLETED — manual action required |
| Application Insights | `quotes-api-insights` | Receives traces, metrics and logs from QuotesApi | NOT COMPLETED — manual action required |
| Key Vault | `quotes-api-kv-<suffix>` | Stores the Application Insights connection string as a secret | NOT COMPLETED — manual action required |
| Action group | `quotes-api-oncall` | Email notification target for alerts | NOT COMPLETED — manual action required |
| Alert rule | `quotes-api-post-quotes-slow-response` | Pages when POST /api/quotes is slow | NOT COMPLETED — manual action required |

`scripts/setup-azure-monitor.sh` creates all of the above idempotently
(checks for an existing resource before creating one) once you have the
Azure CLI installed and `az login` completed:

```bash
ALERT_EMAIL="you@example.com" ./scripts/setup-azure-monitor.sh
```

The alert email address is intentionally not committed anywhere in this
repo — pass it as an environment variable when you run the script, or
enter it directly in the Portal if you create the action group by hand.

## How the connection string flows (never hardcoded)

1. **Key Vault** stores a secret named `ApplicationInsightsConnectionString`
   (Key Vault secret names cannot contain underscores, which is why this
   isn't literally `APPLICATIONINSIGHTS_CONNECTION_STRING`).
2. At startup, `Program.cs` reads `KeyVault:VaultUri` from configuration.
   If it's set, it calls `builder.Configuration.AddAzureKeyVault(vaultUri,
   new DefaultAzureCredential())`, which pulls every secret from the vault
   into configuration. The Key Vault secret `ApplicationInsightsConnectionString`
   becomes the configuration key `ApplicationInsightsConnectionString`.
3. `Program.cs` then reads the connection string as:
   ```csharp
   builder.Configuration["ApplicationInsightsConnectionString"]
       ?? builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
   ```
   so either the Key Vault secret **or** the standard
   `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable/user-secret
   works.
4. If neither is present, `useAzureMonitor` is `false` and Azure Monitor
   is skipped entirely — the app runs locally with only the Aspire/OTLP
   pipeline, no Azure credentials required.

The connection string is never written to `appsettings.json`,
`appsettings.Development.json`, source code, or any committed file.

### Local development

Nothing is required to run locally — `KeyVault:VaultUri` is empty in
`appsettings.json`, so Key Vault and Azure Monitor are both inactive by
default and the app behaves exactly as it did before this change (Aspire
only).

To test the Azure Monitor path locally without Key Vault, use a user
secret (never committed, already supported — `UserSecretsId` is already
configured in the `.csproj`):

```bash
dotnet user-secrets set "ApplicationInsightsConnectionString" "<connection string from the Portal>"
```

or an environment variable for a single run:

```bash
APPLICATIONINSIGHTS_CONNECTION_STRING="<connection string>" dotnet run
```

### Production

Set `KeyVault:VaultUri` (via `KeyVault__VaultUri` environment variable, or
your hosting platform's configuration/app settings — never in a committed
file) to the vault's URI, e.g. `https://quotes-api-kv-xxxx.vault.azure.net/`.
The app authenticates to Key Vault with `DefaultAzureCredential`, which in
order tries: managed identity (when deployed to an Azure host with one
assigned), then `az login` credentials, then Visual Studio/VS Code
credentials, etc. — no client secret is created or embedded anywhere.

Grant whichever identity runs the app the **Key Vault Secrets User** RBAC
role on the vault (the setup script does this for the currently
signed-in `az` user; do the equivalent for the deployed app's managed
identity once there is a hosting target).

## OpenTelemetry wiring

`Program.cs` keeps a single OpenTelemetry pipeline and fans it out to two
destinations rather than building two independent pipelines:

- **Always on** (local + production): `AddSource("QuotesApi")`,
  `AddEntityFrameworkCoreInstrumentation()`, and the OTLP exporter to
  `http://localhost:4317` for the Aspire dashboard.
- **Local only** (`useAzureMonitor == false`, i.e. no connection string
  configured anywhere): `AddAspNetCoreInstrumentation()` and
  `AddHttpClientInstrumentation()` are added by our own `WithTracing()`
  block, exactly as before.
- **Azure Monitor active** (`useAzureMonitor == true`): instead,
  `UseAzureMonitor()` is called. It registers ASP.NET Core and HttpClient
  instrumentation itself, plus trace/metric/log exporters to Application
  Insights.

The `AddAspNetCoreInstrumentation()` / `AddHttpClientInstrumentation()`
calls are deliberately conditional on `useAzureMonitor` — `UseAzureMonitor()`
already registers both. Adding them a second time on the same
`TracerProviderBuilder` would double-instrument ASP.NET Core requests and
outbound HTTP calls, producing duplicate spans in whichever exporter is
active. This is the one structural change beyond "just add the package":
everything else — the custom `ActivitySource`, the custom span, EF Core
instrumentation, and the Aspire OTLP exporter — is untouched.

Metrics and logs are handled entirely by `UseAzureMonitor()` when active;
no separate `WithMetrics()`/`WithLogging()` call was added, since none
existed before this change and the Distro sets both up automatically.

### Serilog → Azure Monitor logs

Serilog fully owns the app's `ILogger` pipeline via `UseSerilog(...)`. By
default, Serilog does **not** forward events to other registered
`ILoggerProvider`s (this is what let it fully replace the default
console/debug providers). Azure Monitor's logging integration is exactly
one of those other providers — added internally by `UseAzureMonitor()` —
so without a change here, `ILogger.LogInformation(...)` calls would keep
reaching the Aspire OTLP sink and the console, but never Application
Insights.

Two small, targeted changes fix this:

1. `builder.Logging.ClearProviders()` right after `CreateBuilder` — drops
   the default ASP.NET Core console/debug providers before anything else
   runs. Serilog already fully overrode these when `writeToProviders` was
   `false`, so this has no visible effect on today's behavior.
2. `UseSerilog(..., writeToProviders: true)` — makes Serilog additionally
   forward every log event to whatever `ILoggerProvider`s end up
   registered. Locally that's still "none" (harmless no-op, verified by
   running the app — no duplicate console lines). In production, that's
   the Azure Monitor logging provider, so `ILogger` calls (Serilog's own
   and any framework-emitted ones) reach Application Insights `traces`.

This was verified locally: `dotnet run`, then `POST /api/quotes` with a
valid JWT, produced exactly one console log line —
`[TraceId:...] [SpanId:...] Created quote 7 for user 1` — with no
duplicate output.

## Custom span

The existing `ActivitySource("QuotesApi")` and the `application-processing`
custom span (with its `application.component`, `http.method`, and
`http.path` tags) in `Program.cs` are untouched. `AddSource("QuotesApi")`
is still registered on the tracer provider, so the span is captured by
whichever exporters are attached to that provider — the Aspire OTLP
exporter locally, and additionally the Azure Monitor trace exporter once
a connection string is configured. This was **not** independently
verified against a live Application Insights resource (none exists yet);
locally, the span still appears correctly in the Aspire dashboard and its
TraceId/SpanId show up correctly correlated in the Serilog console output
(see the verification log line above).

## Structured UserId logging

`Extensions/QuoteEndpointExtensions.cs`, `POST /api/quotes` handler: the
endpoint already requires authorization, so the authenticated user's `sub`
JWT claim (set at login time to the real `User.Id` — see
`Extensions/AuthEndpointExtensions.cs`) is available via a bound
`ClaimsPrincipal` parameter. The existing log line was extended from:

```csharp
logger.LogInformation("Created quote {QuoteId}", createdQuote.Id);
```

to:

```csharp
var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
    ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

logger.LogInformation("Created quote {QuoteId} for user {UserId}", createdQuote.Id, userId);
```

`{UserId}` is a real authenticated user identifier (the internal numeric
`User.Id`, whichever JWT scheme — internal or Entra — validated the
request), never a fabricated value. No password, JWT, signing key, or
connection string is ever logged. Because of the Serilog change above,
this structured property becomes a `customDimensions.UserId` field on the
resulting Application Insights `traces` row, matching the KQL query in
`docs/application-insights-queries.kql`.

## KQL queries

See `docs/application-insights-queries.kql` for the full set: the
`UserId`-filtered trace query from the task, recent requests, POST
`/api/quotes`-only requests, average response time (5-minute buckets),
failed requests, exceptions, dependencies, and the custom
`application-processing` span. None of these have been run against real
data — no Application Insights resource exists in this environment — but
the syntax matches the current Application Insights table schema
(`requests`, `traces`, `dependencies`, `exceptions`; `requests.duration`
is already in milliseconds).

## Alert: POST /api/quotes average response time > 500ms / 5min

Configured as an Azure Monitor scheduled query alert (the current
supported alert type for Application Insights, superseding classic
metric alerts for this kind of custom query) in
`scripts/setup-azure-monitor.sh`:

- **Query**: `requests | where name contains "POST /api/quotes" | summarize AvgDurationMs = avg(duration)`
  (`duration` is already milliseconds in the current schema — no unit
  conversion needed).
- **Window / evaluation frequency**: 5 minutes / 5 minutes.
- **Threshold**: `avg AvgDurationMs > 500`.
- **Action**: email, via an action group (`quotes-api-oncall`).
- **Auto-mitigate**: enabled, so the alert resolves itself once the
  average drops back under 500ms — it's a signal that action is (or was)
  required, not a permanent page.
- **Severity**: 2 (warning-level), not 0/1 — this is a latency
  degradation, not an outage. Keep genuinely page-worthy conditions
  (error-rate spikes, full outage) at a higher severity if you add more
  alerts later; "everything else is a dashboard" per the task, so resist
  adding more email alerts without a clear "someone needs to act now"
  justification.

### Safe verification (do NOT break the app to test this)

Once the alert exists in Azure:

1. Generate legitimate load against `POST /api/quotes` for a few minutes
   (a simple loop with `curl`/`hey`/`k6` against a valid JWT) and watch
   the average duration in the KQL query — it should stay well under
   500ms against local SQLite, so this alone won't fire it, which is the
   correct/expected (non-noisy) behavior.
2. To actually exercise the alert path without harming production code,
   temporarily point the alert's own test at a **non-production**
   environment/slot, or use Azure Monitor's "Fire test alert" / preview
   panel in the Portal if available for the metric/log alert type, which
   simulates a firing without needing real slow traffic.
3. If you want a genuine (still safe) slow-response signal, add a
   short, explicitly test-only artificial delay behind a
   feature-flag/environment check that only activates in a disposable
   test environment — never in code that runs in production — and remove
   it afterward. This wasn't added here since no such throwaway
   environment exists yet; flagging it as an option rather than adding
   permanent test-only behavior to `Program.cs`.

## Verification performed

- `dotnet restore` / `dotnet build` — succeeded (0 errors).
- `dotnet run` — app starts, migrates the SQLite database, listens on
  `http://localhost:5228`.
- `GET /api/quotes` — 200 OK, returned existing quotes.
- `POST /api/auth/login` — 200 OK, returned a JWT.
- `POST /api/quotes` with that JWT — 201 Created, and the console log
  showed `Created quote 7 for user 1` with a populated `TraceId`/`SpanId`
  (confirming the custom span is still active).
- `dotnet test Quotes.Tests.Integration` — all 17 existing integration
  tests still pass.
- Azure resource existence, Key Vault secret existence/access, and live
  Application Insights ingestion were **not** verified — there is no
  Azure CLI session and no provisioned resource in this environment.
