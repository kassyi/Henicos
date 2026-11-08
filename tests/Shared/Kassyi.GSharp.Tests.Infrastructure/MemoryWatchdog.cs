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
/// <param name="collectSnapshot">Writes a memory snapshot and describes it in one sentence; started once, on the first
/// breach.</param>
/// <param name="snapshotWait">How long a failing test waits for the snapshot before reporting without it.</param>
public sealed class MemoryWatchdog(
    string assemblyName,
    long budgetBytes,
    string peakMetricName,
    Func<long> readPeakBytes,
    Func<long> readGcHeapBytes,
    Func<string>? collectSnapshot = null,
    TimeSpan snapshotWait = default)
{
    private const long BytesPerMegabyte = 1024 * 1024;
    private const int TopGrowthCount = 20;
    private const int TopGrowthInMessage = 5;

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _peakAtStart = new(StringComparer.Ordinal);
    private readonly List<PeakGrowth> _topGrowth = [];
    private HashSet<string>? _unreported;
    private string _runningAtBreach = string.Empty;
    private long _peakBytes;
    private long _gcPeakBytes;
    private long _gcHeapBytes;
    private long _testsStarted;
    private string? _error;
    private Task<string>? _snapshot;
    private long _budgetBytes = budgetBytes;
    private bool _stressProfile;

    /// <summary>
    /// Gets the budget in bytes; <c>0</c> when the assembly has no budget.
    /// </summary>
    public long BudgetBytes
    {
        get
        {
            lock (_gate)
            {
                return _budgetBytes;
            }
        }
    }

    /// <summary>
    /// Gets the budget profile in use: <c>stress</c> once a Stress or Benchmark test has started, otherwise
    /// <c>default</c>.
    /// </summary>
    public string Profile
    {
        get
        {
            lock (_gate)
            {
                return _stressProfile ? "stress" : "default";
            }
        }
    }

    /// <summary>
    /// Switches to the budget of runs that include Stress or Benchmark tests, before the first of them starts.
    /// </summary>
    /// <param name="stressBudgetBytes">The stress budget in bytes; a value not above the current budget keeps it.</param>
    public void EnterStressProfile(long stressBudgetBytes)
    {
        lock (_gate)
        {
            if (_stressProfile)
            {
                return;
            }

            _stressProfile = true;
            // The peak never falls, so a smaller stress budget would fail tests for growth the first budget allowed
            if (stressBudgetBytes > _budgetBytes && _budgetBytes > 0)
            {
                _budgetBytes = stressBudgetBytes;
            }
        }
    }

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
    /// Gets the number of tests started so far; <c>0</c> in a process that loads a test assembly without running its
    /// tests.
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
            _gcHeapBytes = gcHeap;
            if (_unreported is not null || _budgetBytes <= 0 || _peakBytes <= _budgetBytes)
            {
                return;
            }

            var names = new List<string>(_running.Keys);
            names.Sort(StringComparer.Ordinal);
            _unreported = new HashSet<string>(names, StringComparer.Ordinal);
            _runningAtBreach = names.Count == 0 ? "none (the breach happened between tests)" : string.Join(", ", names);
            _snapshot = collectSnapshot is null ? null : Task.Run(collectSnapshot);
        }
    }

    /// <summary>
    /// Gets the tests that raised the peak the most, largest first (at most 20).
    /// </summary>
    /// <remarks>
    /// The growth is the rise of the high-water mark between the start and the end of a test. Under parallel execution
    /// it includes what the tests running beside it allocated, so it names candidates rather than proving a cause.
    /// </remarks>
    public IReadOnlyList<PeakGrowth> TopGrowth
    {
        get
        {
            lock (_gate)
            {
                return _topGrowth.ToArray();
            }
        }
    }

    /// <summary>
    /// Records that a test started, after taking a sample.
    /// </summary>
    /// <param name="testName">The test name.</param>
    public void TestStarting(string testName)
    {
        Sample();
        lock (_gate)
        {
            _testsStarted++;
            _peakAtStart[testName] = _peakBytes;
            _running[testName] = _running.TryGetValue(testName, out int count) ? count + 1 : 1;
        }
    }

    /// <summary>
    /// Records that a test finished, after taking a sample.
    /// </summary>
    /// <param name="testName">The test name.</param>
    /// <returns>The failure message when the test was running at the breach and has not reported it yet; otherwise <see
    /// langword="null"/>.</returns>
    public string? TestFinished(string testName)
    {
        Sample();
        string? message;
        lock (_gate)
        {
            if (_peakAtStart.TryGetValue(testName, out long peakAtStart))
            {
                RecordGrowth(new PeakGrowth(testName, _peakBytes - peakAtStart, _gcHeapBytes));
            }

            if (_running.TryGetValue(testName, out int count))
            {
                if (count <= 1)
                {
                    _running.Remove(testName);
                    _peakAtStart.Remove(testName);
                }
                else
                {
                    _running[testName] = count - 1;
                }
            }

            message = _unreported?.Remove(testName) == true ? BreachMessage() : null;
        }

        return message is null ? null : message + DescribeSnapshot();
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
    /// <returns>The failure message when the peak exceeded the budget, the assembly has no budget, or sampling failed;
    /// otherwise <see langword="null"/>.</returns>
    public string? WholeRunFailure()
    {
        Sample();
        string? breach;
        lock (_gate)
        {
            if (_error is not null)
            {
                return $"The memory budget watchdog of {assemblyName} failed: {_error}";
            }

            if (_budgetBytes <= 0)
            {
                return $"{assemblyName} has no memory budget. Add '{assemblyName}: <MB>' to "
                    + $"{MemoryBudgetFile.RelativePath} (see .agents/rules/test-execution.md). {Describe()}";
            }

            breach = _unreported is null ? null : BreachMessage();
        }

        return breach is null ? null : breach + DescribeSnapshot();
    }

    // Waits outside the lock, so that the sampler and the other tests go on while the snapshot is written
    private string DescribeSnapshot()
    {
        Task<string>? snapshot;
        lock (_gate)
        {
            snapshot = _snapshot;
        }

        if (snapshot is null)
        {
            return string.Empty;
        }

        try
        {
            return snapshot.Wait(snapshotWait)
                ? " " + snapshot.Result
                : $" The memory snapshot did not finish in {snapshotWait.TotalSeconds:0} s.";
        }
        catch (AggregateException ex)
        {
            return $" The memory snapshot failed: {ex.InnerException?.Message ?? ex.Message}";
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
            string budget = _budgetBytes > 0 ? $"budget {Megabytes(_budgetBytes)} MB ({Profile} profile)" : "no budget";
            return $"{peakMetricName} {Megabytes(_peakBytes)} MB, GC heap peak {Megabytes(_gcPeakBytes)} MB, {budget}.";
        }
    }

    private void RecordGrowth(PeakGrowth growth)
    {
        if (growth.GrowthBytes <= 0)
        {
            return;
        }

        int index = 0;
        while (index < _topGrowth.Count && _topGrowth[index].GrowthBytes >= growth.GrowthBytes)
        {
            index++;
        }

        if (index == _topGrowth.Count)
        {
            if (_topGrowth.Count < TopGrowthCount)
            {
                _topGrowth.Add(growth);
            }

            return;
        }

        _topGrowth.Insert(index, growth);
        if (_topGrowth.Count > TopGrowthCount)
        {
            _topGrowth.RemoveAt(TopGrowthCount);
        }
    }

    private string BreachMessage() =>
        $"The test host of {assemblyName} exceeded its memory budget: {Describe()} "
        + $"Tests running when the breach was seen: {_runningAtBreach}. "
        + $"Largest growth of the peak during a test: {DescribeTopGrowth()}. "
        + $"Budgets are in {MemoryBudgetFile.RelativePath} (see .agents/rules/test-execution.md).";

    private string DescribeTopGrowth()
    {
        if (_topGrowth.Count == 0)
        {
            return "none";
        }

        var parts = new List<string>(TopGrowthInMessage);
        for (int i = 0; i < _topGrowth.Count && i < TopGrowthInMessage; i++)
        {
            PeakGrowth growth = _topGrowth[i];
            string gcHeap = Megabytes(growth.GcHeapBytes);
            parts.Add($"{growth.TestName} +{Megabytes(growth.GrowthBytes)} MB (GC heap {gcHeap} MB)");
        }

        return string.Join(", ", parts);
    }

    private static string Megabytes(long bytes) =>
        (bytes / BytesPerMegabyte).ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The rise of the memory high-water mark during one test.
/// </summary>
/// <param name="TestName">The test name.</param>
/// <param name="GrowthBytes">The rise of the peak between the start and the end of the test, in bytes.</param>
/// <param name="GcHeapBytes">The managed heap size at the end of the test, in bytes.</param>
public readonly record struct PeakGrowth(string TestName, long GrowthBytes, long GcHeapBytes);
