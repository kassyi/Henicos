using Task = System.Threading.Tasks.Task;

namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>Standard process execution helper that prevents stdout/stderr pipe buffer deadlocks.</summary>
public class ProcessRunner : IProcessRunner
{
    /// <summary>Gets the default shared process runner instance.</summary>
    public static readonly ProcessRunner Default = new();

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;

        var process = new Process
        {
            StartInfo = startInfo
        };

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => tcs.TrySetResult(true);

        if (cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException();
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {startInfo.FileName}");
        }

        using var ctr = cancellationToken.Register(state =>
        {
            if (state is Process p)
            {
                tcs.TrySetCanceled();
                try
                {
                    if (p is { HasExited: false })
                    {
                        p.Kill();
                    }
                }
                catch
                {
                    // Ignore process cleanup errors on best-effort basis during cancellation
                }
            }
        }, process);

#if NET8_0_OR_GREATER
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
#else
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
#endif

        try
        {
            await Task.WhenAll(tcs.Task, stdoutTask, stderrTask).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        }
        finally
        {
            process.Dispose();
        }
    }

    public ProcessResult Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        // MSBuild tasks usually run synchronously.
        // We use GetAwaiter().GetResult() but since RunAsync reads asynchronously via Task.WhenAll, 
        // the deadlock risk between stdout and stderr buffer filling is eliminated.
        return RunAsync(startInfo, cancellationToken).GetAwaiter().GetResult();
    }
}
