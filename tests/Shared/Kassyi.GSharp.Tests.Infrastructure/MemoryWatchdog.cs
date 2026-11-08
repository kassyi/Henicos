#nullable enable
using System.Globalization;

namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Tracks the memory peak of a test host against its budget and decides which tests a breach fails.
/// </summary>
/// <remarks>
/// The first sample above the budget records the tests running at that moment; each of them fails when it finishes.
/// The peak is a high-water mark, so the test that caused the growth is always among them: either a sample sees the
/// breach while it runs, or its own <see cref="TestFinished"/> sample does. Growth outside any test (fixture
/// construction, disposal) is reported by <see cref="WholeRunFailure"/>.
/// </remarks>
/// <param name="assemblyName">The test assembly name, shown in failure messages.</param>
/// <param name="budgetBytes">The budget in bytes; <c>0</c> when the assembly has no budget.</param>
/// <param name="peakMetricName">The name of the metric <paramref name="readPeakBytes"/> returns.</param>
/// <param name="readPeakBytes">Reads the memory high-water mark of the process.</param>
/// <param name="readGcHeapBytes">Reads the current managed heap size.</param>
public sealed class MemoryWatchdog(
    string assemblyName,
    long budgetBytes,
    string peakMetricName,
    Func<long> readPeakBytes,
    Func<long> readGcHeapBytes)
{
    private const long BytesPerMegabyte = 1024 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _running = new(StringComparer.Ordinal);
    private HashSet<string>? _unreported;
    private string _runningAtBreach = string.Empty;
    private long _peakBytes;
    private long _gcPeakBytes;
    private long _testsStarted;
    private string? _error;

    /// <summary>
    /// Gets the budget in bytes; <c>0</c> when the assembly has no budget.
    /// </summary>
    public long BudgetBytes => budgetBytes;

    /// <summary>
    /// Gets the highest peak seen so far, in bytes.
    /// </summary>
    public long PeakBytes
    {
        get
        {
            lock (_gate)
            {
                return _peakBytes;
            }
        }
    }

    /// <summary>
    /// Gets the highest managed heap size seen so far, in bytes.
    /// </summary>
    public long GcPeakBytes
    {
        get
        {
            lock (_gate)
            {
                return _gcPeakBytes;
            }
        }
    }

    /// <summary>
    /// Gets the number of tests started so far; <c>0</c> in a process that loads a test assembly without running its tests.
    /// </summary>
    public long TestsStarted
    {
        get
        {
            lock (_gate)
            {
                return _testsStarted;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the peak has exceeded the budget.
    /// </summary>
    public bool Breached
    {
        get
        {
            lock (_gate)
            {
                return _unreported is not null;
            }
        }
    }

    /// <summary>
    /// Takes one sample and records a breach when the peak exceeds the budget for the first time.
    /// </summary>
    public void Sample()
    {
        long peak = readPeakBytes();
        long gcHeap = readGcHeapBytes();
        lock (_gate)
        {
            _peakBytes = Math.Max(_peakBytes, peak);
            _gcPeakBytes = Math.Max(_gcPeakBytes, gcHeap);
            if (_unreported is not null || budgetBytes <= 0 || _peakBytes <= budgetBytes)
            {
                return;
            }

            var names = new List<string>(_running.Keys);
            names.Sort(StringComparer.Ordinal);
            _unreported = new HashSet<string>(names, StringComparer.Ordinal);
            _runningAtBreach = names.Count == 0 ? "none (the breach happened between tests)" : string.Join(", ", names);
        }
    }

    /// <summary>
    /// Records that a test started.
    /// </summary>
    /// <param name="testName">The test name.</param>
    public void TestStarting(string testName)
    {
        lock (_gate)
        {
            _testsStarted++;
            _running[testName] = _running.TryGetValue(testName, out int count) ? count + 1 : 1;
        }
    }

    /// <summary>
    /// Records that a test finished, after taking a sample.
    /// </summary>
    /// <param name="testName">The test name.</param>
    /// <returns>The failure message when the test was running at the breach and has not reported it yet; otherwise <see langword="null"/>.</returns>
    public string? TestFinished(string testName)
    {
        Sample();
        lock (_gate)
        {
            if (_running.TryGetValue(testName, out int count))
            {
                if (count <= 1)
                {
                    _running.Remove(testName);
                }
                else
                {
                    _running[testName] = count - 1;
                }
            }

            return _unreported?.Remove(testName) == true ? BreachMessage() : null;
        }
    }

    /// <summary>
    /// Records an error that stopped the sampling, reported by <see cref="WholeRunFailure"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    public void RecordError(string message)
    {
        lock (_gate)
        {
            _error ??= message;
        }
    }

    /// <summary>
    /// Takes a sample and checks the whole run so far.
    /// </summary>
    /// <returns>The failure message when the peak exceeded the budget, the assembly has no budget, or sampling failed; otherwise <see langword="null"/>.</returns>
    public string? WholeRunFailure()
    {
        Sample();
        lock (_gate)
        {
            if (_error is not null)
            {
                return $"The memory budget watchdog of {assemblyName} failed: {_error}";
            }

            if (budgetBytes <= 0)
            {
                return $"{assemblyName} has no memory budget. Add '{assemblyName}: <MB>' to {MemoryBudgetFile.RelativePath} (see .agents/rules/test-execution.md). {Describe()}";
            }

            return _unreported is null ? null : BreachMessage();
        }
    }

    /// <summary>
    /// Describes the peaks and the budget in one sentence.
    /// </summary>
    /// <returns>The description.</returns>
    public string Describe()
    {
        lock (_gate)
        {
            string budget = budgetBytes > 0 ? $"budget {Megabytes(budgetBytes)} MB" : "no budget";
            return $"{peakMetricName} {Megabytes(_peakBytes)} MB, GC heap peak {Megabytes(_gcPeakBytes)} MB, {budget}.";
        }
    }

    private string BreachMessage() =>
        $"The test host of {assemblyName} exceeded its memory budget: {Describe()} "
        + $"Tests running when the breach was seen: {_runningAtBreach}. "
        + $"Budgets are in {MemoryBudgetFile.RelativePath} (see .agents/rules/test-execution.md).";

    private static string Megabytes(long bytes) =>
        (bytes / BytesPerMegabyte).ToString(CultureInfo.InvariantCulture);
}
