// Fix: the producer injects the active trace context into the message's carrier
// (a Dictionary<string,string> riding alongside the payload — the "baggage" the
// slide refers to). The consumer extracts it back into Activity.Current before
// starting its activity, so the activity joins the producer's trace.

using System.Diagnostics;
using System.Threading.Channels;
using DemoTrace;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

const string ServiceName = "instrumentation-traps-async-boundary-after";
var activitySource = new ActivitySource(ServiceName);

using var provider = DemoTracing.Setup(ServiceName);

// message carries an explicit trace-context carrier alongside the payload.

var propagator = Propagators.DefaultTextMapPropagator;
var channel = Channel.CreateBounded<Message>(1);
var consumed = new TaskCompletionSource();

_ = Task.Run(async () =>
{
    var msg = await channel.Reader.ReadAsync();

    // FIXED: extract the trace context from the carrier before starting the activity
    var parentContext = propagator.Extract(
        default,
        msg.Carrier,
        (dict, key) => dict.TryGetValue(key, out var val)
            ? new[] { val }
            : Enumerable.Empty<string>());

    using var activity = activitySource.StartActivity(
        "consume_order_message",
        ActivityKind.Consumer,
        parentContext.ActivityContext);

    Console.WriteLine($"consumer trace ID:  {Activity.Current?.TraceId} (same as producer) order={msg.OrderId}");
    await Task.Delay(5);
    consumed.SetResult();
});

using var produceSpan = activitySource.StartActivity("produce_order_message");
Console.WriteLine($"producer trace ID: {Activity.Current?.TraceId}");

// Inject the current trace context into the carrier before putting the message
// on the channel. The carrier is a plain dictionary — the "baggage" the slide
// refers to.
var carrier = new Dictionary<string, string>();
propagator.Inject(
    new PropagationContext(Activity.Current?.Context ?? default, Baggage.Current),
    carrier,
    (dict, key, value) => dict[key] = value);

await channel.Writer.WriteAsync(new Message("ord_42", carrier));
produceSpan?.Stop();

await consumed.Task;
Console.WriteLine("FIXED: the carrier crossed the channel boundary, so the consumer's activity joins the producer's trace.");


// message carries an explicit trace-context carrier alongside the payload.
record Message(string OrderId, Dictionary<string, string> Carrier);
