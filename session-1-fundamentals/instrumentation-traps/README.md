# instrumentation-traps

Three pairs of before/after programs that demonstrate common instrumentation
mistakes and their idiomatic C# fixes.

## The traps

### 1. context-propagation

| | What happens |
|---|---|
| **before** | `ValidateOrder` uses `default(ActivityContext)` as explicit parent, so its span starts a new root trace instead of being a child |
| **after** | Remove the explicit `parentContext` argument; `StartActivity` picks up `Activity.Current` as the ambient parent |

### 2. async-boundary

| | What happens |
|---|---|
| **before** | Channel carries only `record Message(string OrderId)` — no trace context. Consumer's span starts a disconnected new trace |
| **after** | Message carries `Dictionary<string,string> Carrier`. Producer calls `Propagators.DefaultTextMapPropagator.Inject` before sending; consumer calls `Extract` before starting its span |

### 3. noisy-library

| | What happens |
|---|---|
| **before** | `AddSource(VendorSourceName)` tells the SDK to record the vendor library's spans, flooding every trace |
| **after** | Simply omit `AddSource(VendorSourceName)`. The vendor's `ActivitySource.StartActivity` returns `null` when no provider has subscribed — no spans, no cost |

> **C# differs from Go here.** In Go, the fix passes a `noop.TracerProvider` to
> the vendor client. In C#, `ActivitySource.StartActivity` returns `null` when
> no `TracerProvider` has called `AddSource(vendorSourceName)`. The fix is to
> omit the `AddSource` call — the vendor library is silenced automatically by
> the .NET null-activity contract.

## Running

```bash
cd context-propagation/before/ && dotnet run
cd context-propagation/after/  && dotnet run
```

All six programs require the collector to be running (see session-1 README).

## Shared OTel setup

`DemoTrace/DemoTrace.cs` contains the shared `DemoTracing.Setup()` helper that
bootstraps a `TracerProvider` with OTLP export. This keeps each before/after
program focused on a single trapping concept.

> **C# cleanup note:** In C#, `using var provider = DemoTracing.Setup(...)`
> calls `Dispose()` → `Shutdown(Timeout.Infinite)` when the scope ends, which
> flushes all pending spans. No explicit `ForceFlush` call is needed. This is
> the C# idiomatic equivalent of Go's `defer tp.Shutdown(ctx)`.
