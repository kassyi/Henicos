#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Isolates CUDA device <c>trap;</c> fault seeds in a child process so that sticky
/// <c>CUDA_ERROR_ILLEGAL_INSTRUCTION</c> errors never poison the parent <c>testhost</c> <c>CUcontext</c>.
/// </summary>
public static class CudaTrapSubprocessRunner
{
    /// <summary>Environment variable set on the isolated child process to select a single <c>Case#ShapeIndex:Seed</c> target.</summary>
    public const string TargetEnvVar = "GSHARP_DIFF_CUDA_TRAP_TARGET";

    /// <summary>Environment variable set on the isolated child process pointing to the file where the structured trap result is written.</summary>
    public const string ResultFileEnvVar = "GSHARP_DIFF_CUDA_TRAP_RESULT_FILE";

    /// <summary>Environment variable enabling all fault seeds/shapes on CUDA instead of <see cref="CudaTrapSampling.FirstFaultPerCase"/>.</summary>
    public const string FullTrapsEnvVar = "GSHARP_DIFF_FULL_CUDA_TRAPS";

    private const string ResultPrefix = "GSHARP_CUDA_TRAP_RESULT|";

    // [WHY] a test that plays the child must not set the process-wide variables: every differential test running in
    // parallel would then see itself as a child and return an empty report, so it would pass without comparing anything
    private static readonly AsyncLocal<(string Target, string ResultFile)?> s_simulatedChild = new();

    /// <summary>Gets a value indicating whether the current process is an isolated CUDA trap child process.</summary>
    public static bool IsChildProcess => !string.IsNullOrEmpty(ReadTarget());

    /// <summary>
    /// Makes the current asynchronous flow behave as the child process for <paramref name="target"/>, writing its result
    /// to <paramref name="resultFile"/>, until the returned scope is disposed. Other tests running in parallel are not affected.
    /// </summary>
    internal static SimulatedChildScope SimulateChild(string target, string resultFile)
    {
        var previous = s_simulatedChild.Value;
        s_simulatedChild.Value = (target, resultFile);
        return new SimulatedChildScope(previous);
    }

    private static string? ReadTarget() =>
        s_simulatedChild.Value?.Target ?? Environment.GetEnvironmentVariable(TargetEnvVar);

    private static string? ReadResultFile() =>
        s_simulatedChild.Value?.ResultFile ?? Environment.GetEnvironmentVariable(ResultFileEnvVar);

    /// <summary>Restores the previous simulated child, if any, when disposed.</summary>
    internal readonly struct SimulatedChildScope((string Target, string ResultFile)? previous) : IDisposable
    {
        public void Dispose() => s_simulatedChild.Value = previous;
    }

    /// <summary>Gets a value indicating whether full CUDA trap sampling is enabled via <see cref="FullTrapsEnvVar"/>.</summary>
    public static bool IsFullSamplingEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(FullTrapsEnvVar), "1", StringComparison.Ordinal);

    /// <summary>Parses the active <see cref="TargetEnvVar"/> target, if running inside a child process.</summary>
    public static bool TryGetActiveTarget(out (string CaseName, int ShapeIndex, int Seed) target)
    {
        string? raw = ReadTarget();
        if (string.IsNullOrEmpty(raw))
        {
            target = default;
            return false;
        }

        int hashIdx = raw.LastIndexOf('#');
        int colonIdx = raw.LastIndexOf(':');
        if (hashIdx <= 0 || colonIdx <= hashIdx + 1 || colonIdx >= raw.Length - 1)
        {
            target = default;
            return false;
        }

        string caseName = raw[..hashIdx];
        if (!int.TryParse(raw[(hashIdx + 1)..colonIdx], NumberStyles.Integer, CultureInfo.InvariantCulture, out int shapeIndex) ||
            !int.TryParse(raw[(colonIdx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
        {
            target = default;
            return false;
        }

        target = (caseName, shapeIndex, seed);
        return true;
    }

    /// <summary>Formats the target environment variable value for a specific case, shape index, and seed.</summary>
    public static string FormatTarget(string caseName, int shapeIndex, int seed) =>
        string.Create(CultureInfo.InvariantCulture, $"{caseName}#{shapeIndex}:{seed}");

    /// <summary>
    /// Writes the structured trap result from the child process to <paramref name="resultFilePath"/>
    /// (or the path in <see cref="ResultFileEnvVar"/> when <paramref name="resultFilePath"/> is <see langword="null"/>).
    /// </summary>
    internal static void WriteChildResult(Exception? thrown, string? resultFilePath = null)
    {
        string line = FormatResultLine(thrown);
        string? targetFile = resultFilePath ?? ReadResultFile();
        if (!string.IsNullOrEmpty(targetFile))
        {
            File.WriteAllText(targetFile, line, Encoding.UTF8);
        }
    }

    /// <summary>Reads and reconstructs the child trap result from <paramref name="resultFilePath"/>.</summary>
    internal static bool TryReadChildResult(string resultFilePath, out Exception? thrown)
    {
        if (string.IsNullOrEmpty(resultFilePath) || !File.Exists(resultFilePath))
        {
            thrown = null;
            return false;
        }

        return TryParseResultLine(File.ReadAllText(resultFilePath, Encoding.UTF8), out thrown);
    }

    internal static string FormatResultLine(Exception? thrown)
    {
        if (thrown is null)
        {
            return $"{ResultPrefix}NONE|||||";
        }

        var actual = ExceptionPolicy.Unwrap(thrown);
        string exType = actual.GetType().FullName ?? actual.GetType().Name;
        string innerType = actual.InnerException switch
        {
            SubprocessCudaInnerException sub => sub.OriginalTypeName,
            { } inner => inner.GetType().FullName ?? inner.GetType().Name,
            null => "",
        };
        string memberName = actual.Data["GSharp.MemberName"] as string ?? "";
        string sourceLoc = actual.Data["GSharp.SourceLocation"] as string ?? "";
        string paramName = actual is ArgumentException argEx ? (argEx.ParamName ?? "") : "";
        string message = Convert.ToBase64String(Encoding.UTF8.GetBytes(actual.Message));
        return $"{ResultPrefix}THREW|{exType}|{innerType}|{memberName}|{sourceLoc}|{paramName}|{message}";
    }

    internal static bool TryParseResultLine(string content, out Exception? thrown)
    {
        foreach (string rawLine in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith(ResultPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string payload = line[ResultPrefix.Length..];
            string[] parts = payload.Split('|');
            if (parts.Length >= 1 && parts[0] == "NONE")
            {
                thrown = null;
                return true;
            }

            if (parts.Length >= 7 && parts[0] == "THREW")
            {
                string exTypeName = parts[1];
                string innerTypeName = parts[2];
                string memberName = parts[3];
                string sourceLoc = parts[4];
                string paramName = parts[5];
                string message = Encoding.UTF8.GetString(Convert.FromBase64String(parts[6]));
                thrown = ReconstructException(exTypeName, innerTypeName, memberName, sourceLoc, paramName, message);
                return true;
            }
        }

        thrown = null;
        return false;
    }

    /// <summary>
    /// Runs the specified <c>(caseName, shapeIndex, seed)</c> in an isolated child <c>dotnet vstest</c> process
    /// and returns the reconstructed exception (or <see langword="null"/> if the child launch completed without throwing).
    /// </summary>
    public static Exception? RunInSubprocess(string caseName, int shapeIndex, int seed, int timeoutMs = 60_000)
    {
        var (testAssemblyPath, fullTestMethodName) = ResolveCallingTestMethod();
        string targetValue = FormatTarget(caseName, shapeIndex, seed);
        string resultFilePath = Path.Combine(Path.GetTempPath(), $"gsharp-cuda-trap-{Guid.NewGuid():N}.txt");

        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("vstest");
        psi.ArgumentList.Add(testAssemblyPath);
        psi.ArgumentList.Add($"--Tests:{fullTestMethodName}");
        psi.Environment[TargetEnvVar] = targetValue;
        psi.Environment[ResultFileEnvVar] = resultFilePath;
        psi.Environment["DiffEngine_Disabled"] = "true";
        psi.Environment["CI"] = "true";

        try
        {
            var (exitCode, stdout, stderr, timedOut) = RunProcessWithTimeout(psi, timeoutMs);
            if (timedOut)
            {
                throw new DifferentialHarnessException(
                    $"CUDA trap subprocess timed out after {timeoutMs.ToString(CultureInfo.InvariantCulture)}ms for {targetValue}."
                );
            }

            if (TryReadChildResult(resultFilePath, out var reconstructed))
            {
                return reconstructed;
            }

            throw new DifferentialHarnessException(
                $"CUDA trap subprocess for {targetValue} (test {fullTestMethodName}) exited with code {exitCode} without writing {ResultPrefix} to {resultFilePath}.\nstdout:\n{stdout}\nstderr:\n{stderr}"
            );
        }
        finally
        {
            if (File.Exists(resultFilePath))
            {
                try
                {
                    File.Delete(resultFilePath);
                }
                // smell-scan: ignore swallowed-exception -- the result is already read and the failure goes to Trace; a
                // leftover temp file must not turn the case into a failure
                catch (IOException ex)
                {
                    Trace.WriteLine($"Could not delete the CUDA trap result file {resultFilePath}: {ex.Message}");
                }
            }
        }
    }

    internal static (int ExitCode, string Stdout, string Stderr, bool TimedOut) RunProcessWithTimeout(ProcessStartInfo psi, int timeoutMs)
    {
        using var process = Process.Start(psi)
            ?? throw new DifferentialHarnessException($"Failed to start subprocess '{psi.FileName}'.");

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            // smell-scan: ignore swallowed-exception -- the process terminated between the timeout check and Kill; the
            // timeout is reported by the caller
            catch (InvalidOperationException)
            {
            }

            return (-1, string.Empty, string.Empty, TimedOut: true);
        }

        process.WaitForExit();
        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        return (process.ExitCode, stdout, stderr, TimedOut: false);
    }

    private static (string AssemblyPath, string FullMethodName) ResolveCallingTestMethod()
    {
        var stackTrace = new StackTrace(fNeedFileInfo: false);
        for (int i = 0; i < stackTrace.FrameCount; i++)
        {
            var method = stackTrace.GetFrame(i)?.GetMethod();
            if (method is null || method.DeclaringType is null)
            {
                continue;
            }

            bool isTestMethod =
                method.GetCustomAttribute<FactAttribute>(inherit: true) is not null ||
                method.GetCustomAttribute<TheoryAttribute>(inherit: true) is not null;
            if (isTestMethod)
            {
                string assemblyPath = method.DeclaringType.Assembly.Location;
                string fullMethodName = $"{method.DeclaringType.FullName}.{method.Name}";
                return (assemblyPath, fullMethodName);
            }
        }

        throw new DifferentialHarnessException(
            "Could not locate the calling [Fact] or [Theory] test method on the call stack for CudaTrapSubprocessRunner."
        );
    }

    private static Exception ReconstructException(
        string exTypeName,
        string innerTypeName,
        string memberName,
        string sourceLoc,
        string paramName,
        string message
    )
    {
        Exception? inner = string.IsNullOrEmpty(innerTypeName)
            ? null
            : new SubprocessCudaInnerException(innerTypeName);

        Exception ex = exTypeName switch
        {
            "System.IndexOutOfRangeException" => new IndexOutOfRangeException(message, inner),
            "System.DivideByZeroException" => new DivideByZeroException(message, inner),
            "System.OverflowException" => new OverflowException(message, inner),
            "System.NullReferenceException" => new NullReferenceException(message, inner),
            "System.InvalidOperationException" => new InvalidOperationException(message, inner),
            "System.NotSupportedException" => new NotSupportedException(message, inner),
            "System.ArithmeticException" => new ArithmeticException(message, inner),
            "System.ArgumentNullException" => inner is null
                ? new ArgumentNullException(paramName, message)
                : new ArgumentNullException(message, inner),
            "System.ArgumentOutOfRangeException" => inner is null
                ? new ArgumentOutOfRangeException(paramName, message)
                : new ArgumentOutOfRangeException(message, inner),
            "System.ArgumentException" => new ArgumentException(message, paramName, inner),
            _ => CreateByTypeOrFallback(exTypeName, message, inner),
        };

        if (!string.IsNullOrEmpty(memberName))
        {
            ex.Data["GSharp.MemberName"] = memberName;
        }

        if (!string.IsNullOrEmpty(sourceLoc))
        {
            ex.Data["GSharp.SourceLocation"] = sourceLoc;
        }

        return ex;
    }

    private static Exception CreateByTypeOrFallback(string exTypeName, string message, Exception? inner)
    {
        var type = Type.GetType(exTypeName, throwOnError: false);
        if (type is not null && typeof(Exception).IsAssignableFrom(type))
        {
            try
            {
                if (Activator.CreateInstance(type, message, inner) is Exception created)
                {
                    return created;
                }
            }
            // smell-scan: ignore swallowed-exception -- the exception type has no usable (string, Exception)
            // constructor, so the code below wraps the message in a generic exception
            catch (Exception ex) when (ex is MissingMethodException or MemberAccessException or TargetInvocationException)
            {
            }
        }

        return new Exception($"{exTypeName}: {message}", inner);
    }

    internal sealed class SubprocessCudaInnerException(string originalTypeName)
        : Exception($"Subprocess CUDA inner exception ({originalTypeName})")
    {
        public string OriginalTypeName { get; } = originalTypeName;
    }
}
