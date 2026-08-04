# otel-quickstart

A minimal ASP.NET Core Minimal APIs service that demonstrates the three-beat
OTel SDK bootstrapping pattern live during the session.

## Running

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
dotnet run --project OtelQuickstart/
```

The service exposes:
- `GET /healthz` — health check
- `POST /api/checkout` — checkout endpoint (body: `{"orderId":"ord_42"}`)

## Three-Beat Structure

The code comments mark three beats that the instructor reveals one at a time:

| Beat | What lands | Key teaching |
|---|---|---|
| 1 | Bootstrap + auto-instrumentation | SDK wiring, `AddAspNetCoreInstrumentation()` |
| 2 | Manual span | `ActivitySource`, `StartActivity`, `SetTag` |
| 3 | Span attribute on current | `Activity.Current?.SetTag()` — ambient context |

> **C# note:** ASP.NET Core's `AddAspNetCoreInstrumentation()` automatically sets
> `http.route` from the Minimal API route template (e.g., `/api/checkout`).
> The Go version uses `otelhttp` which does NOT set `http.route` — the Go code
> has an explicit `setRoute()` helper. Beat 1 in C# therefore includes
> `http.route` for free.

## Tests

```bash
dotnet test OtelQuickstart.Tests/
```

`TestCheckoutTraceShape` pins the span shape at every beat.
`TestInboundTraceparentIsJoined` proves W3C trace-context propagation works.

Both tests use `WebApplicationFactory<Program>` with a
`SimpleActivityExportProcessor` (synchronous, no queue) so spans arrive
in the in-memory list immediately without needing `ForceFlush`.
