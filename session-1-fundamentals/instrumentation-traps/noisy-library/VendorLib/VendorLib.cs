// VendorLib is a stand-in for a third-party dependency (an ORM, an HTTP client,
// an SDK) that instruments its own internals heavily. It's not part of the trap
// or the fix — both sides of the demo use it unchanged; only the
// TracerProvider handed to the ActivitySource differs.

using System.Diagnostics;

namespace VendorLib;

public class Client
{
    private readonly ActivitySource _tracer;

    public Client(string activitySourceName)
    {
        _tracer = new ActivitySource(activitySourceName);
    }

    // Do simulates a call that, internally, is four activities' worth of
    // plumbing most callers never need to see.
    public void Do()
    {
        foreach (var (name, delayMs) in new[] {
            ("acquire_connection", 2),
            ("serialize", 1),
            ("network_write", 3),
            ("network_read", 3),
        })
        {
            using var span = _tracer.StartActivity(name);
            Thread.Sleep(delayMs);
        }
    }
}
