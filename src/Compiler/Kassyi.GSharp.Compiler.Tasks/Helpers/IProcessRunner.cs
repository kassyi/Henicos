namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>Represents the exit code and captured standard streams of an executed process.</summary>
public readonly struct ProcessResult(int exitCode, string standardOutput, string? standardError)
{
    /// <summary>Gets the process exit code.</summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>Gets the captured standard output text.</summary>
    public string StandardOutput { get; } = standardOutput;

    /// <summary>Gets the captured standard error text.</summary>
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
