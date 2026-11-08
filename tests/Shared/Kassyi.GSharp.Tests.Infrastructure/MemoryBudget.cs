#nullable enable
using System.Globalization;

namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Runs the memory budget watchdog of a test host: one <see cref="MemoryWatchdog"/> per process, sampled every
/// <see cref="SampleIntervalMilliseconds"/> on a background thread and at the end of every test.
/// </summary>
/// <remarks>
/// Every test assembly starts it from a module initializer that <c>tests/Directory.Build.props</c> compiles in
/// (<c>TestAssemblySources/MemoryBudgetHooks.cs</c>), together with <see cref="MemoryBudgetTrackingAttribute"/> and the
/// final check that runs last (<see cref="MemoryBudgetCollectionOrderer"/>).
/// </remarks>
public static class MemoryBudget
{
    /// <summary>
    /// The sampling interval of the background thread.
    /// </summary>
    public const int SampleIntervalMilliseconds = 250;

    /// <summary>
    /// The environment variable that overrides the budget of the assembly, in MB.
    /// </summary>
    public const string BudgetVariable = "GSHARP_TEST_MEMORY_BUDGET_MB";

    /// <summary>
    /// The environment variable naming a directory to which each test host appends its peaks when its final check runs
    /// (<c>&lt;assembly&gt;.tsv</c>), used to measure the budgets.
    /// </summary>
    public const string ReportVariable = "GSHARP_TEST_MEMORY_REPORT";

    /// <summary>
    /// The name of the test collection of the final check. It runs after every other collection.
    /// </summary>
    public const string FinalCollection = "MemoryBudgetFinal";

    private const long BytesPerMegabyte = 1024 * 1024;
    private static readonly object s_gate = new();
    private static MemoryWatchdog? s_current;
    private static string s_assemblyName = string.Empty;

    /// <summary>
    /// Gets the watchdog of this process, or <see langword="null"/> before <see cref="Start"/>.
    /// </summary>
    public static MemoryWatchdog? Current => Volatile.Read(ref s_current);

    /// <summary>
    /// Starts the watchdog of this process with the budget of <paramref name="assemblyName"/>. Later calls do nothing.
    /// </summary>
    /// <param name="assemblyName">The name of the test assembly.</param>
    public static void Start(string assemblyName)
    {
        lock (s_gate)
        {
            if (s_current is not null)
            {
                return;
            }

            string? error = null;
            long budgetMegabytes = 0;
            try
            {
                budgetMegabytes = ReadBudgetMegabytes(assemblyName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                error = ex.Message;
            }

            var watchdog = new MemoryWatchdog(
                assemblyName,
                budgetMegabytes * BytesPerMegabyte,
                ProcessMemory.PeakMetricName,
                ProcessMemory.ReadPeakBytes,
                ProcessMemory.ReadGcHeapBytes
            );
            if (error is not null)
            {
                watchdog.RecordError(error);
            }

            s_assemblyName = assemblyName;
            Volatile.Write(ref s_current, watchdog);
            var thread = new Thread(() => SampleLoop(watchdog))
            {
                IsBackground = true,
                Name = "Memory budget watchdog",
            };
            thread.Start();
        }
    }

    /// <summary>
    /// Checks the whole run so far, and appends the peaks to the report when <see cref="ReportVariable"/> is set: the
    /// final check of every test assembly calls it after every other test.
    /// </summary>
    /// <returns>The failure message, or <see langword="null"/> when the peak stayed within the budget.</returns>
    public static string? WholeRunFailure()
    {
        if (Current is not { } watchdog)
        {
            return "The memory budget watchdog was not started (MemoryBudgetHooks.cs is missing from this test assembly).";
        }

        string? failure = watchdog.WholeRunFailure();
        AppendReport(watchdog);
        return failure;
    }

    private static long ReadBudgetMegabytes(string assemblyName)
    {
        string? overrideText = Environment.GetEnvironmentVariable(BudgetVariable);
        if (!string.IsNullOrEmpty(overrideText))
        {
            return long.TryParse(overrideText, NumberStyles.None, CultureInfo.InvariantCulture, out long value) && value > 0
                ? value
                : throw new FormatException($"{BudgetVariable} must be a positive number of MB, got '{overrideText}'.");
        }

        foreach (KeyValuePair<string, long> entry in MemoryBudgetFile.Parse(File.ReadAllText(MemoryBudgetFile.FullPath)))
        {
            if (entry.Key == assemblyName)
            {
                return entry.Value;
            }
        }

        return 0;
    }

    private static void SampleLoop(MemoryWatchdog watchdog)
    {
        try
        {
            while (true)
            {
                watchdog.Sample();
                Thread.Sleep(SampleIntervalMilliseconds);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            watchdog.RecordError(ex.Message);
        }
    }

    // Appends "<peak MB>\t<GC heap peak MB>\t<budget MB>\t<tests>\t<metric>" to <dir>/<assembly>.tsv when
    // GSHARP_TEST_MEMORY_REPORT is set. Written from the final check rather than at process exit: the runner may end
    // the test host without raising ProcessExit.
    private static void AppendReport(MemoryWatchdog watchdog)
    {
        string? directory = Environment.GetEnvironmentVariable(ReportVariable);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        string line = string.Join(
            "\t",
            (watchdog.PeakBytes / BytesPerMegabyte).ToString(CultureInfo.InvariantCulture),
            (watchdog.GcPeakBytes / BytesPerMegabyte).ToString(CultureInfo.InvariantCulture),
            (watchdog.BudgetBytes / BytesPerMegabyte).ToString(CultureInfo.InvariantCulture),
            watchdog.TestsStarted.ToString(CultureInfo.InvariantCulture),
            ProcessMemory.PeakMetricName
        );
        File.AppendAllText(Path.Combine(directory, s_assemblyName + ".tsv"), line + Environment.NewLine);
    }
}
