#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Decides whether the tests that raise a real GPU fault (a CUDA <c>trap;</c> in a child process) run, and paces the
/// child processes that do.
/// </summary>
/// <remarks>
/// Each fault logs a driver error, resets the GPU engine and, on Windows (WDDM), writes a live kernel dump; a run of
/// such faults has frozen a whole machine for seconds at a time (#1457). These tests carry
/// <c>TestCategory.GpuTrap</c>, which the default <c>just</c> filters leave out, and they run only when
/// <see cref="EnableVariable"/> is <c>1</c> (<c>just test-gpu-trap</c> sets it), so a plain <c>dotnet test</c> raises
/// no fault either. Inside one test process the child processes run one at a time, at least
/// <see cref="PauseVariable"/> milliseconds apart.
/// </remarks>
public static class CudaTrapGate
{
    /// <summary>The environment variable that enables the tests raising a real GPU fault when it is <c>1</c>.</summary>
    public const string EnableVariable = "GSHARP_CUDA_TRAP_TESTS";

    /// <summary>The environment variable that sets the pause between two faults, in milliseconds.</summary>
    public const string PauseVariable = "GSHARP_CUDA_TRAP_PAUSE_MS";

    /// <summary>The pause between two faults when <see cref="PauseVariable"/> is not set.</summary>
    public const int DefaultPauseMs = 3000;

    /// <summary>Why a test or a differential cell that would raise a real GPU fault did not run.</summary>
    public const string SkipReason =
        "raises a real GPU fault (CUDA trap), which can freeze a Windows machine; run `just test-gpu-trap` ("
        + EnableVariable + "=1)";

    private static readonly Lock s_gate = new();
    private static long s_lastEnd;

    /// <summary>Gets whether the tests raising a real GPU fault are enabled.</summary>
    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal);

    /// <summary>
    /// Runs <paramref name="runChild"/>, which starts one child process that faults, after every other such child of
    /// this process has ended and the pause has passed since the last one ended.
    /// </summary>
    public static T RunPaced<T>(Func<T> runChild)
    {
        lock (s_gate)
        {
            if (s_lastEnd != 0)
            {
                var wait = TimeSpan.FromMilliseconds(PauseMs) - Stopwatch.GetElapsedTime(s_lastEnd);
                if (wait > TimeSpan.Zero)
                {
                    Thread.Sleep(wait);
                }
            }

            try
            {
                return runChild();
            }
            finally
            {
                s_lastEnd = Stopwatch.GetTimestamp();
            }
        }
    }

    private static int PauseMs =>
        int.TryParse(
            Environment.GetEnvironmentVariable(PauseVariable),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int pause
        ) && pause >= 0
            ? pause
            : DefaultPauseMs;
}
