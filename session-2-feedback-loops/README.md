# Session 2 – Feedback Loops

This session demonstrates how to spot a canary regression using a Honeycomb
dataset seeded with before/after deploy data.

## Exercises

| Directory | What it teaches |
|---|---|
| [`SeedCanaryRegression/`](SeedCanaryRegression/) | Seed a dataset showing a 40% p95 regression in a small affected population |
| [`honeycomb-setup/`](honeycomb-setup/) | Terraform / shell scripts to provision the Honeycomb environment |

## Key concept

The regression is invisible in aggregate: only 10% of requests route through
the affected code path, and the new behaviour adds latency only at the tail
(40% slower p95). Mean and p50 look identical to the control population.

The canary test uses a BubbleUp-style drill-down to surface the population
where the regression is concentrated.

## Running the seeder

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
dotnet run --project SeedCanaryRegression/SeedCanaryRegression/
```

## Tests

```bash
dotnet test SeedCanaryRegression/SeedCanaryRegression.Tests/
```

`ScenarioTests` verifies the statistical properties of the generated dataset:
- Affected population is realistically small (~10%)
- Regression is invisible in aggregate metrics
- Affected p95 is ~40% slower

`DeliveryTests` verifies that chunked flushing delivers the full volume.
