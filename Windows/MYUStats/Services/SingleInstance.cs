using Microsoft.UI.Dispatching;

namespace MYUStats;

/// <summary>
/// One MYU STATS per sign-in. A second launch finds the first through a named mutex and asks it,
/// through a named event, to open Settings.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\MYUStats.Instance";
    private const string EventName = @"Local\MYUStats.OpenSettings";

    private readonly Mutex mutex;
    private EventWaitHandle? request;
    private volatile bool disposed;

    private SingleInstance(Mutex mutex, bool isFirst)
    {
        this.mutex = mutex;
        IsFirst = isFirst;
    }

    public bool IsFirst { get; }

    public static SingleInstance Claim()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        return new SingleInstance(mutex, created);
    }

    /// <summary>From a second launch: asks the running copy to open Settings.</summary>
    public void SignalFirst()
    {
        if (!EventWaitHandle.TryOpenExisting(EventName, out var handle)) return;
        // This launch is in the foreground; let the running copy bring Settings to the front.
        Interop.NativeMethods.AllowSetForegroundWindow(Interop.NativeMethods.AsfwAny);
        using (handle) handle.Set();
    }

    /// <summary>In the running copy: calls <paramref name="onRequest"/> on the UI thread whenever another launch asks.</summary>
    public void Listen(DispatcherQueue queue, Action onRequest)
    {
        request = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var waiting = request;
        var listener = new Thread(() =>
        {
            try
            {
                while (!disposed && waiting.WaitOne())
                {
                    if (!disposed) queue.TryEnqueue(() => onRequest());
                }
            }
            catch (ObjectDisposedException)
            {
                // Quitting.
            }
        })
        {
            IsBackground = true,
            Name = "MYU STATS launch listener",
        };
        listener.Start();
    }

    public void Dispose()
    {
        disposed = true;
        request?.Dispose();
        if (IsFirst) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
