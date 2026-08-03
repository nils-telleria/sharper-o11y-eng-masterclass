# Observability Engineering Masterclass — C#

Code and Honeycomb examples accompanying the six-session masterclass based on
*Observability Engineering* (2nd Edition, O'Reilly), fully migrated to C# /
ASP.NET Core from the original Go source at
[honeycombio/o11y-eng-masterclass](https://github.com/honeycombio/o11y-eng-masterclass).

See [`masterclass-curriculum-45min.md`](masterclass-curriculum-45min.md) for the
full curriculum.

Each session gets its own directory:

- [`session-1-fundamentals/`](session-1-fundamentals) — Wide Events &
  Instrumentation with OTel
- [`session-2-feedback-loops/`](session-2-feedback-loops) — Observability Connects
  Code to Delivery
- [`session-3-business-case/`](session-3-business-case) — The Business Case and
  the Investment Diagnostic

## Requirements

- .NET 10 SDK
- Docker (for the local OTel Collector)
- A Honeycomb API key with send-events permission, for the Collector
- A Honeycomb configuration key, if you want to create markers/boards/SLOs —
  a different key from the one above
- Terraform 1.5+ (optional; every Honeycomb object also has a shell-script path)

## Verifying

```bash
make verify      # format-check, build, test across every project
make tidy        # dotnet restore across every project
```

The solution file (`sharper-o11y-eng-masterclass.sln`) references every project.

## C# / .NET specifics

### ASP.NET Core Minimal APIs

`session-1-fundamentals/otel-quickstart` uses [ASP.NET Core Minimal APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis?view=aspnetcore-10.0)
as the web framework. Endpoints are registered with `app.MapGet` / `app.MapPost`
rather than a traditional controller or router.

### OTel SDK mapping

| Go | C# |
|---|---|
| `otel.Tracer(name)` | `new ActivitySource(name)` |
| `tracer.Start(ctx, "name")` | `activitySource.StartActivity("name")` |
| `span.SetAttributes(kv...)` | `activity.SetTag("key", value)` |
| `span.SetStatus(codes.Error, msg)` | `activity.SetStatus(ActivityStatusCode.Error, msg)` |
| `span.End()` | `activity.Dispose()` (via `using`) |
| `trace.SpanContextFromContext(ctx).TraceID()` | `Activity.Current?.TraceId` |
| `sdktrace.WithBatcher(exporter)` | `BatchExportActivityProcessor` (default) |
| `sdktrace.WithSyncer(exporter)` | `SimpleExportActivityProcessor` (for tests) |
| `tracetest.NewInMemoryExporter()` | `AddInMemoryExporter(list)` |

### One difference from Go's otel-quickstart

Go's `otelhttp` does **not** set `http.route` automatically — the Go README calls
this out explicitly and `setRoute()` adds it manually. ASP.NET Core's OTel
instrumentation (`AddAspNetCoreInstrumentation`) **does** set `http.route` from
the Minimal API route template, so beat 1 already includes it. The demo has been
adapted accordingly; `http.route` is still highlighted in the session notes as an
important attribute, but no manual enrichment is needed.

## Delivery model

Each masterclass is a 45-minute live session, run from pre-built and
pre-seeded demos — nothing is built from scratch on stage. The same
materials also work as a longer, self-serve async lab, or as raw material
for a longer instructor-led workshop; those formats aren't time-boxed to 45
minutes. Every session directory calls out which parts are "pre-session
setup," which are "live demo," and which are lab/workshop material.
