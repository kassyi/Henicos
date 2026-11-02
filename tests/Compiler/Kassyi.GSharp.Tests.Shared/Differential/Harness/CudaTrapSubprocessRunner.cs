#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
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

    /// <summary>Environment variable enabling all fault seeds/shapes on CUDA instead of <see cref="CudaTrapSampling.FirstFaultPerCase"/>.</summary>
    public const string FullTrapsEnvVar = "GSHARP_DIFF_FULL_CUDA_TRAPS";

    private const string ResultPrefix = "GSHARP_CUDA_TRAP_RESULT|";

    /// <summary>Gets a value indicating whether the current process is an isolated CUDA trap child process.</summary>
    public static bool IsChildProcess => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(TargetEnvVar));

    /// <summary>Gets a value indicating whether full CUDA trap sampling is enabled via <see cref="FullTrapsEnvVar"/>.</summary>
    public static bool IsFullSamplingEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(FullTrapsEnvVar), "1", StringComparison.Ordinal);

    /// <summary>Parses the active <see cref="TargetEnvVar"/> target, if running inside a child process.</summary>
    public static bool TryGetActiveTarget(out (string CaseName, int ShapeIndex, int Seed) target)
    {
        string? raw = Environment.GetEnvironmentVariable(TargetEnvVar);
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

    /// <summary>Writes the structured trap result line from the child process to standard output.</summary>
    internal static void WriteChildResult(Exception? thrown)
    {
        if (thrown is null)
        {
            Console.WriteLine($"{ResultPrefix}NONE|||||");
            return;
        }

        var actual = ExceptionPolicy.Unwrap(thrown);
        string exType = actual.GetType().FullName ?? actual.GetType().Name;
        string innerType = actual.InnerException?.GetType().FullName ?? "";
        string memberName = actual.Data["GSharp.MemberName"] as string ?? "";
        string sourceLoc = actual.Data["GSharp.SourceLocation"] as string ?? "";
        string paramName = actual is ArgumentException argEx ? (argEx.ParamName ?? "") : "";
        string message = Convert.ToBase64String(Encoding.UTF8.GetBytes(actual.Message));
        Console.WriteLine($"{ResultPrefix}THREW|{exType}|{innerType}|{memberName}|{sourceLoc}|{paramName}|{message}");
    }

    /// <summary>
    /// Runs the specified <c>(caseName, shapeIndex, seed)</c> in an isolated child <c>dotnet vstest</c> process
    /// and returns the reconstructed exception (or <see langword="null"/> if the child launch completed without throwing).
    /// </summary>
    public static Exception? RunInSubprocess(string caseName, int shapeIndex, int seed)
    {
        var (testAssemblyPath, fullTestMethodName) = ResolveCallingTestMethod();
        string targetValue = FormatTarget(caseName, shapeIndex, seed);

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
        psi.Environment["DiffEngine_Disabled"] = "true";
        psi.Environment["CI"] = "true";

        using var process = Process.Start(psi)
            ?? throw new DifferentialHarnessException($"Failed to start dotnet vstest subprocess for {targetValue}.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(60_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already terminated between timeout check and Kill.
            }

            throw new DifferentialHarnessException($"CUDA trap subprocess timed out after 60s for {targetValue}.");
        }

        foreach (string rawLine in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
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
                return null;
            }

            if (parts.Length >= 7 && parts[0] == "THREW")
            {
                string exTypeName = parts[1];
                string innerTypeName = parts[2];
                string memberName = parts[3];
                string sourceLoc = parts[4];
                string paramName = parts[5];
                string message = Encoding.UTF8.GetString(Convert.FromBase64String(parts[6]));
                return ReconstructException(exTypeName, innerTypeName, memberName, sourceLoc, paramName, message);
            }
        }

        throw new DifferentialHarnessException(
            $"CUDA trap subprocess for {targetValue} (test {fullTestMethodName}) exited with code {process.ExitCode} without emitting {ResultPrefix}.\nstdout:\n{stdout}\nstderr:\n{stderr}"
        );
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
            catch (Exception ex) when (ex is MissingMethodException or MemberAccessException or TargetInvocationException)
            {
                // Fall back to a generic wrapper if the exception type has no (string, Exception) constructor.
            }
        }

        return new Exception($"{exTypeName}: {message}", inner);
    }

    private sealed class SubprocessCudaInnerException(string originalTypeName)
        : Exception($"Subprocess CUDA inner exception ({originalTypeName})");
}
