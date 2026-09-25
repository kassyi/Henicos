namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

public readonly struct ProcessResult(int exitCode, string standardOutput, string? standardError)
{
    public int ExitCode { get; } = exitCode;
    public string StandardOutput { get; } = standardOutput;
    public string StandardError { get; } = standardError ?? string.Empty;
}

public interface IProcessRunner
{
    /// <summary>
    /// Executes a process asynchronously and safely reads its standard output and error streams to prevent deadlocks.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a process synchronously and safely reads its standard output and error streams to prevent deadlocks.
    /// </summary>
    ProcessResult Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default);
}
