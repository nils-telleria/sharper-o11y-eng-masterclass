# seed-sample-service

A console program that seeds a realistic checkout dataset into any OTLP
backend, using historical timestamps so the data looks like production traffic.

## Running

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
dotnet run --project SeedSampleService/
```

By default, seeds 2500 traces over a 4-hour window ending now.

## Historical timestamps

Uses `ActivitySource.StartActivity(..., startTime: offset)` to place spans in
the past, then `activity.SetEndTime(end)` before dispose so the batch exporter
records the historical end time rather than the wall-clock time at export.

## Chunked flushing

Spans are emitted in chunks of 400 with `provider.ForceFlush()` between chunks.
This is required because the `BatchExportActivityProcessor` queue defaults to
8192 entries. Without chunking, all spans above that limit are dropped silently
and the seeder exits zero having delivered only a fraction of the dataset.

## Tests

```bash
dotnet test SeedSampleService.Tests/
```

`TraceInvariantsTests` runs 500 seeded traces and checks:
- Exactly one root per trace
- Error status agrees with payment outcome
- Success (4 children) and failure (3 children) branches both appear

`DeliveryTests` runs 2500 traces (above the 8192 queue limit when multiplied
by spans-per-trace) and asserts that chunked flushing delivers every root.
