// Trap: a producer starts an activity, then hands work to a Task (standing in
// for a background worker, message queue consumer, or cron run) via a channel
// that only carries the payload. The consumer has no way to know a trace was
// ever in progress, so its activity starts a disconnected new trace.

using System.Diagnostics;
using System.Threading.Channels;
using DemoTrace;
using OpenTelemetry.Context.Propagation;

const string ServiceName = "instrumentation-traps-async-boundary-before";
var activitySource = new ActivitySource(ServiceName);

using var provider = DemoTracing.Setup(ServiceName);

// message is what crosses the queue boundary — payload only.
record Message(string OrderId);

var channel = Channel.CreateBounded<Message>(1);
var consumed = new TaskCompletionSource();

_ = Task.Run(async () =>
{
    var msg = await channel.Reader.ReadAsync();

    // BUG: no context carrier crossed the boundary, so this starts a new trace
    using var activity = activitySource.StartActivity("consume_order_message");
    Console.WriteLine($"consumer trace ID:  {Activity.Current?.TraceId} (different from producer!) order={msg.OrderId}");
    await Task.Delay(5);
    consumed.SetResult();
});

using var produceSpan = activitySource.StartActivity("produce_order_message");
Console.WriteLine($"producer trace ID: {Activity.Current?.TraceId}");
await channel.Writer.WriteAsync(new Message("ord_42"));
produceSpan?.Stop();

await consumed.Task;
Console.WriteLine("BUG: the channel only carried the payload, so the consumer's activity is a disconnected new trace.");

provider.ForceFlush(5_000);
