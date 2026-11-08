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
    /// The environment variable that, set to <c>1</c>, makes the final check also write a memory snapshot
    /// (<see cref="MemoryDump"/>) of what the run left on the heap, named in <c>&lt;assembly&gt;.snapshot.txt</c> of
    /// the report.
    /// </summary>
    public const string DumpAtEndVariable = "GSHARP_TEST_MEMORY_DUMP_AT_END";

    /// <summary>
    /// The name of the test collection of the final check. It runs after every other collection.
    /// </summary>
    public const string FinalCollection = "MemoryBudgetFinal";

    private const long BytesPerMegabyte = 1024 * 1024;
    private static readonly object s_gate = new();
    private static MemoryWatchdog? s_current;
    private static string s_assemblyName = string.Empty;
    private static long s_stressBudgetBytes;

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
            MemoryBudgetEntry budget = default;
            try
            {
                budget = ReadBudget(assemblyName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                error = ex.Message;
            }

            var watchdog = new MemoryWatchdog(
                assemblyName,
                budget.Megabytes * BytesPerMegabyte,
                ProcessMemory.PeakMetricName,
                ProcessMemory.ReadPeakBytes,
                ProcessMemory.ReadGcHeapBytes,
                () => MemoryDump.Collect(assemblyName, "breach"),
                MemoryDump.Timeout + TimeSpan.FromSeconds(10)
            );
            if (error is not null)
            {
                watchdog.RecordError(error);
            }

            s_assemblyName = assemblyName;
            s_stressBudgetBytes = budget.StressMegabytes * BytesPerMegabyte;
            Volatile.Write(ref s_current, watchdog);
            var thread = new Thread(() => SampleLoop(watchdog))
            {
                IsBackground = true,
                Name = "Memory budget watchdog",
            };
            thread.Start();
            WriteProcessId(assemblyName);
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
            return "The memory budget watchdog was not started "
                + "(MemoryBudgetHooks.cs is missing from this test assembly).";
        }

        string? failure = watchdog.WholeRunFailure();
        AppendReport(watchdog);
        if (Environment.GetEnvironmentVariable(DumpAtEndVariable) == "1"
            && Environment.GetEnvironmentVariable(ReportVariable) is { Length: > 0 } directory)
        {
            string snapshot = MemoryDump.Collect(s_assemblyName, "end");
            File.WriteAllText(Path.Combine(directory, s_assemblyName + ".snapshot.txt"), snapshot);
        }

        return failure;
    }

    // <dir>/<assembly>.pid names the test host for `just test-memory`, which attaches dotnet-counters to it. The first
    // process wins: a helper process that a test starts later and that loads the assembly too does not replace it.
    private static void WriteProcessId(string assemblyName)
    {
        string? directory = Environment.GetEnvironmentVariable(ReportVariable);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, assemblyName + ".pid");
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            string pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            byte[] text = System.Text.Encoding.ASCII.GetBytes(pid);
            stream.Write(text, 0, text.Length);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another process of the run wrote it first; see above
            return;
        }
    }

    /// <summary>
    /// Switches the watchdog to the stress budget of the assembly; <see cref="MemoryBudgetTrackingAttribute"/> calls it
    /// before the first Stress or Benchmark test starts.
    /// </summary>
    public static void EnterStressProfile() => Current?.EnterStressProfile(s_stressBudgetBytes);

    // GSHARP_TEST_MEMORY_BUDGET_MB replaces both budgets; an assembly missing from the file gets none (Megabytes 0)
    private static MemoryBudgetEntry ReadBudget(string assemblyName)
    {
        string? overrideText = Environment.GetEnvironmentVariable(BudgetVariable);
        if (!string.IsNullOrEmpty(overrideText))
        {
            bool valid = long.TryParse(overrideText, NumberStyles.None, CultureInfo.InvariantCulture, out long value);
            return valid && value > 0
                ? new MemoryBudgetEntry(assemblyName, value, 0)
                : throw new FormatException($"{BudgetVariable} must be a positive number of MB, got '{overrideText}'.");
        }

        foreach (MemoryBudgetEntry entry in MemoryBudgetFile.Parse(File.ReadAllText(MemoryBudgetFile.FullPath)))
        {
            if (entry.Name == assemblyName)
            {
                return entry;
            }
        }

        return default;
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

    // Appends "<peak MB>\t<GC heap peak MB>\t<budget MB>\t<tests>\t<profile>\t<metric>" to <dir>/<assembly>.tsv when
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
            watchdog.Profile,
            ProcessMemory.PeakMetricName
        );
        File.AppendAllText(Path.Combine(directory, s_assemblyName + ".tsv"), line + Environment.NewLine);

        // <dir>/<assembly>.top.tsv: "# <peak MB>" per run, then "<growth MB>\t<GC heap MB>\t<test>" largest first
        var top = new System.Text.StringBuilder();
        top.Append("# ").Append(watchdog.PeakBytes / BytesPerMegabyte).AppendLine();
        foreach (PeakGrowth growth in watchdog.TopGrowth)
        {
            top.Append(growth.GrowthBytes / BytesPerMegabyte).Append('\t')
                .Append(growth.GcHeapBytes / BytesPerMegabyte).Append('\t')
                .AppendLine(growth.TestName);
        }

        File.AppendAllText(Path.Combine(directory, s_assemblyName + ".top.tsv"), top.ToString());
    }
}
