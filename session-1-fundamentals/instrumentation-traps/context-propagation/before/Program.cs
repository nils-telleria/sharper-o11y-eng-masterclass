// Trap: ValidateOrder doesn't accept an ActivitySource or parent context, so it
// starts a brand-new root activity instead of a child. The span you added
// "never appears in the trace" — it appears in its own trace.

using System.Diagnostics;
using DemoTrace;

const string ServiceName = "instrumentation-traps-context-propagation-before";
var activitySource = new ActivitySource(ServiceName);

using var provider = DemoTracing.Setup(ServiceName);

using var root = activitySource.StartActivity("handle_request");
Console.WriteLine($"handle_request trace ID: {Activity.Current?.TraceId}");
ValidateOrder();
root?.Stop();

Console.WriteLine("BUG: ValidateOrder has no ActivitySource parameter, so its activity starts a new trace instead of joining this one.");


// ValidateOrder takes no ActivitySource or parent context, so it cannot derive
// a child activity from the caller's.
static void ValidateOrder()
{
    // BUG: starts from background (no parent) rather than from Activity.Current
    var source = new ActivitySource("instrumentation-traps-context-propagation-before");
    using var activity = source.StartActivity("validate_order", ActivityKind.Internal,
        parentContext: default); // explicitly no parent — this is the bug
    Console.WriteLine($"validate_order trace ID: {Activity.Current?.TraceId} (different from handle_request!)");
    Thread.Sleep(10);
}
