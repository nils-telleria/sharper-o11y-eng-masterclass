# Session 1 – Fundamentals

This session covers the core OpenTelemetry concepts every engineer needs to know.

## Exercises

| Directory | What it teaches |
|---|---|
| [`otel-quickstart/`](otel-quickstart/) | OTel SDK bootstrap, three-beat live demo |
| [`seed-sample-service/`](seed-sample-service/) | Historical seeder to pre-populate a dataset |
| [`instrumentation-traps/`](instrumentation-traps/) | Three common instrumentation mistakes and their fixes |

## Prerequisites

- .NET 10 SDK
- Docker (for the local Collector)
- A Honeycomb API key (or any OTLP-compatible backend)

## Running the collector

```bash
cd collector/
docker compose up -d
```

The collector listens on `localhost:4317` (gRPC) and `localhost:4318` (HTTP).

## Environment

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
export OTEL_SERVICE_NAME=sample-service
```
