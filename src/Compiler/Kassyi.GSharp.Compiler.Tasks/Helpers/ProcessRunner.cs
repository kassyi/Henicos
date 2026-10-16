using System.ComponentModel;
using Task = System.Threading.Tasks.Task;

namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>Standard process execution helper that prevents stdout/stderr pipe buffer deadlocks.</summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> MSBuild tasks call <see cref="Run"/> from <c>Parallel.ForEach</c> workers. Blocking such a worker on the
/// asynchronous implementation needs another thread-pool thread for its continuations, which starves the pool when many
/// compiles run at once. <see cref="Run"/> therefore performs its I/O synchronously and never touches the pool.
/// </para>
/// </remarks>
public class ProcessRunner : IProcessRunner
{
    /// <summary>Gets the default shared process runner instance.</summary>
    public static readonly ProcessRunner Default = new();

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        ConfigureRedirection(startInfo);

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
                TryKill(p);
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

    /// <inheritdoc />
    /// <remarks>
    /// Standard error is drained on a dedicated thread while standard output is read on the calling thread, so neither
    /// pipe can fill up and block the child. Both reads end at end-of-stream, which is why the output text is identical to
    /// <see cref="RunAsync"/> including trailing newlines. On cancellation the process is killed (without its children,
    /// which <c>netstandard2.0</c> cannot do) and <see cref="OperationCanceledException"/> is thrown.
    /// </remarks>
    public ProcessResult Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfigureRedirection(startInfo);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {startInfo.FileName}");
        }

        var killedByCancellation = 0;
        using var registration = cancellationToken.Register(state =>
        {
            if (state is Process p)
            {
                Interlocked.Exchange(ref killedByCancellation, 1);
                TryKill(p);
            }
        }, process);

        var stderr = string.Empty;
        Exception? stderrFailure = null;
        var stderrReader = new Thread(() =>
        {
            try
            {
                stderr = process.StandardError.ReadToEnd();
            }
            catch (IOException ex)
            {
                // Surfaced after the join below unless the run was canceled, when the broken pipe is expected.
                stderrFailure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "ProcessRunner.StandardError"
        };
        stderrReader.Start();

        var stdout = process.StandardOutput.ReadToEnd();
        stderrReader.Join();

        // The argument-less overload also waits for the asynchronous output handlers to flush.
        process.WaitForExit();

        if (Volatile.Read(ref killedByCancellation) != 0)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (stderrFailure is not null)
        {
            throw new IOException($"Failed to read standard error of {startInfo.FileName}.", stderrFailure);
        }

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static void ConfigureRedirection(ProcessStartInfo startInfo)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited or was disposed between the check and the kill, so there is nothing left to stop.
        }
        catch (Win32Exception)
        {
            // Access denied or already terminating. Cleanup is best-effort; the caller observes the cancellation anyway.
        }
    }
}
