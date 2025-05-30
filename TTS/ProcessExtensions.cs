using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public static class ProcessExtensions
{
    public static async Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>();
        process.EnableRaisingEvents = true;
        process.Exited += (sender, args) => tcs.TrySetResult(true);
        
        if (process.HasExited) return;
        
        using (cancellationToken.Register(() => tcs.TrySetCanceled()))
        {
            await tcs.Task;
        }
    }
}