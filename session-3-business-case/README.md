# Session 3 – Business Case

Tools for building and demonstrating the business case for observability.

## Exercises

| Directory | What it teaches |
|---|---|
| [`cmd/business-case/`](cmd/business-case/) | CLI that builds and prints a cost model |
| [`cmd/seed-arbitrary-question/`](cmd/seed-arbitrary-question/) | Seeder that generates a wide-event checkout dataset |
| [`BusinessCase/`](BusinessCase/) | Library: cost model calculator |
| [`WideEvent/`](WideEvent/) | Library: wide-event generator (30+ attributes per event) |

## Business Case CLI

```bash
dotnet run --project cmd/business-case/ -- \
  --engineers 100 \
  --loaded-cost 260000 \
  --incidents-per-month 10 \
  --mttr-hours 4 \
  --responders 3 \
  --annual-spend 400000
```

Prints a three-scenario table (conservative / moderate / optimistic savings).
Use `--sources` to see the attribution of assumptions.

## Arbitrary Question Seeder

Seeds a `checkout-web` dataset with ~12 000 wide events so the instructor can
answer the "five tests" live:

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
dotnet run --project cmd/seed-arbitrary-question/SeedArbitraryQuestion/
```

The dataset is designed so that filtering by:
- `error = true`
- `user_agent.device = phone`
- `geo.region.iso_code = US-CA`
- `user_agent.app_version = 2.3.1`
- lunch hour (`request.local_hour` 12–13)

returns a small but non-trivial number of matching events.

## Tests

```bash
dotnet test Tests/
```

31 tests cover the cost model arithmetic (`BusinessCaseTests`), wide-event
statistical properties (`WideEventTests`), and end-to-end delivery
(`SeedArbitraryQuestionDeliveryTests`).

## Five tests worksheet

See [`five-tests-worksheet.md`](five-tests-worksheet.md) for the guided
exercise template.
