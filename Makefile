# The solution root contains no runnable code — every project is in a
# session subdirectory. Use the targets here rather than `dotnet build`
# at the root if you want to act on a subset.

PROJECTS := \
	session-1-fundamentals/otel-quickstart/OtelQuickstart/OtelQuickstart.csproj \
	session-1-fundamentals/otel-quickstart/OtelQuickstart.Tests/OtelQuickstart.Tests.csproj \
	session-1-fundamentals/seed-sample-service/SeedSampleService/SeedSampleService.csproj \
	session-1-fundamentals/seed-sample-service/SeedSampleService.Tests/SeedSampleService.Tests.csproj \
	session-1-fundamentals/instrumentation-traps/DemoTrace/DemoTrace.csproj \
	session-1-fundamentals/instrumentation-traps/async-boundary/before/AsyncBoundaryBefore.csproj \
	session-1-fundamentals/instrumentation-traps/async-boundary/after/AsyncBoundaryAfter.csproj \
	session-1-fundamentals/instrumentation-traps/context-propagation/before/ContextPropagationBefore.csproj \
	session-1-fundamentals/instrumentation-traps/context-propagation/after/ContextPropagationAfter.csproj \
	session-1-fundamentals/instrumentation-traps/noisy-library/VendorLib/VendorLib.csproj \
	session-1-fundamentals/instrumentation-traps/noisy-library/before/NoisyLibraryBefore.csproj \
	session-1-fundamentals/instrumentation-traps/noisy-library/after/NoisyLibraryAfter.csproj \
	session-2-feedback-loops/Scenario/Scenario.csproj \
	session-2-feedback-loops/SeedCanaryRegression/SeedCanaryRegression/SeedCanaryRegression.csproj \
	session-2-feedback-loops/SeedCanaryRegression/SeedCanaryRegression.Tests/SeedCanaryRegression.Tests.csproj \
	session-3-business-case/BusinessCase/BusinessCase.csproj \
	session-3-business-case/WideEvent/WideEvent.csproj \
	session-3-business-case/cmd/business-case/BusinessCaseCli.csproj \
	session-3-business-case/cmd/seed-arbitrary-question/SeedArbitraryQuestion/SeedArbitraryQuestion.csproj \
	session-3-business-case/Tests/Session3.Tests.csproj

.PHONY: verify build test fmt-check tidy

verify: fmt-check build test

build:
	@dotnet build sharper-o11y-eng-masterclass.slnx --nologo -q || exit 1

test:
	@dotnet test sharper-o11y-eng-masterclass.slnx --nologo --verbosity quiet || exit 1

fmt-check:
	@dotnet format sharper-o11y-eng-masterclass.slnx --verify-no-changes --no-restore 2>&1 || (echo "dotnet format needed; run 'make fmt' and commit the result"; exit 1)

fmt:
	@dotnet format sharper-o11y-eng-masterclass.slnx --no-restore

tidy:
	@dotnet restore sharper-o11y-eng-masterclass.slnx -q || exit 1
