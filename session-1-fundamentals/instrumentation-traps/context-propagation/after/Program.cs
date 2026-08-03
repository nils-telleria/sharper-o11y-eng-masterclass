// Fix: ValidateOrder takes the activitySource and relies on Activity.Current
// (the ambient context) so its activity becomes a child of whatever activity
// is already active when it's called.

using System.Diagnostics;
using DemoTrace;

const string ServiceName = "instrumentation-traps-context-propagation-after";
var activitySource = new ActivitySource(ServiceName);

using var provider = DemoTracing.Setup(ServiceName);

using var root = activitySource.StartActivity("handle_request");
Console.WriteLine($"handle_request trace ID: {Activity.Current?.TraceId}");
ValidateOrder(activitySource);
root?.Stop();

Console.WriteLine("FIXED: ValidateOrder uses Activity.Current as its parent context, so it joins the same trace.");

provider.ForceFlush(5_000);

// ValidateOrder takes activitySource and lets StartActivity pick up
// Activity.Current as the implicit parent — no explicit parentContext needed.
static void ValidateOrder(ActivitySource activitySource)
{
    // FIXED: StartActivity with no explicit parentContext uses Activity.Current
    using var activity = activitySource.StartActivity("validate_order");
    Console.WriteLine($"validate_order trace ID: {Activity.Current?.TraceId} (same as handle_request)");
    Thread.Sleep(10);
}
