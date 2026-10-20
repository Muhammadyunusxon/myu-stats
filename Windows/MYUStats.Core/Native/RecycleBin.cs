using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;

namespace MYUStats.Native;

/// <summary>Moves files and folders to the Recycle Bin through the shell, which asks before deleting for
/// good anything too big for it.</summary>
[SupportedOSPlatform("windows")]
public sealed class RecycleBin : IRecycler
{
    public void Recycle(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        Exception? failure = null;
        // The shell's file operations expect a single-threaded apartment, which pool threads are not.
        var thread = new Thread(() =>
        {
            try
            {
                Shell32.Recycle(paths);
            }
            catch (Exception error)
            {
                failure = error;
            }
        })
        {
            IsBackground = true,
            Name = "MYU STATS Recycle Bin",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Throw(failure);
    }
}
