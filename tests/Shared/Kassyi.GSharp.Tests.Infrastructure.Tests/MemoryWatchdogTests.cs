#nullable enable
namespace Kassyi.GSharp.Tests.Infrastructure.Tests;

/// <summary>
/// Pins which tests a memory budget breach fails, with a sampler the test controls.
/// </summary>
public class MemoryWatchdogTests
{
    private const long Megabyte = 1024 * 1024;
    private const string Metric = "test peak";

    private long _peak;

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Breach_FailsEveryTestRunningAtThatMoment()
    {
        MemoryWatchdog watchdog = Create(budgetMegabytes: 100);
        watchdog.TestStarting("A.First");
        watchdog.TestStarting("A.Second");

        _peak = 200 * Megabyte;
        watchdog.Sample();
        string? first = watchdog.TestFinished("A.First");
        string? second = watchdog.TestFinished("A.Second");

        Assert.NotNull(first);
        Assert.Contains("test peak 200 MB", first, StringComparison.Ordinal);
        Assert.Contains("budget 100 MB", first, StringComparison.Ordinal);
        Assert.Contains("Tests running when the breach was seen: A.First, A.Second.", first, StringComparison.Ordinal);
        Assert.Equal(first, second);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void GrowthBetweenSamples_IsSeenByTheTestsOwnFinish()
    {
        MemoryWatchdog watchdog = Create(budgetMegabytes: 100);
        watchdog.TestStarting("A.Grows");

        _peak = 101 * Megabyte;
        string? failure = watchdog.TestFinished("A.Grows");

        Assert.NotNull(failure);
        Assert.Contains("A.Grows", failure, StringComparison.Ordinal);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void TestsStartedAfterTheBreach_Pass_AndEachTestReportsOnce()
    {
        MemoryWatchdog watchdog = Create(budgetMegabytes: 100);
        watchdog.TestStarting("A.Running");
        _peak = 150 * Megabyte;
        watchdog.Sample();

        watchdog.TestStarting("A.Later");
        string? later = watchdog.TestFinished("A.Later");
        string? running = watchdog.TestFinished("A.Running");
        watchdog.TestStarting("A.Running");
        string? runningAgain = watchdog.TestFinished("A.Running");

        Assert.Null(later);
        Assert.NotNull(running);
        Assert.Null(runningAgain);
        Assert.True(watchdog.Breached);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void PeakEqualToBudget_IsNotABreach()
    {
        MemoryWatchdog watchdog = Create(budgetMegabytes: 100);
        watchdog.TestStarting("A.AtBudget");

        _peak = 100 * Megabyte;
        string? failure = watchdog.TestFinished("A.AtBudget");

        Assert.Null(failure);
        Assert.Null(watchdog.WholeRunFailure());
        Assert.False(watchdog.Breached);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void BreachBetweenTests_IsReportedByTheWholeRunCheck()
    {
        MemoryWatchdog watchdog = Create(budgetMegabytes: 100);
        watchdog.TestStarting("A.Before");
        Assert.Null(watchdog.TestFinished("A.Before"));

        _peak = 300 * Megabyte;
        watchdog.Sample();
        watchdog.TestStarting("A.After");
        string? after = watchdog.TestFinished("A.After");
        string? wholeRun = watchdog.WholeRunFailure();

        Assert.Null(after);
        Assert.NotNull(wholeRun);
        Assert.Contains("test peak 300 MB", wholeRun, StringComparison.Ordinal);
        Assert.Contains("none (the breach happened between tests)", wholeRun, StringComparison.Ordinal);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void MissingBudget_FailsTheWholeRunCheckOnly()
    {
        MemoryWatchdog watchdog = Create(budgetMegabytes: 0);
        watchdog.TestStarting("A.Any");
        _peak = 4096 * Megabyte;

        string? test = watchdog.TestFinished("A.Any");
        string? wholeRun = watchdog.WholeRunFailure();

        Assert.Null(test);
        Assert.NotNull(wholeRun);
        Assert.Contains("Fixture has no memory budget", wholeRun, StringComparison.Ordinal);
        Assert.Contains(MemoryBudgetFile.RelativePath, wholeRun, StringComparison.Ordinal);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void SamplingError_FailsTheWholeRunCheck()
    {
        MemoryWatchdog watchdog = Create(budgetMegabytes: 100);

        watchdog.RecordError("probe error");
        string? wholeRun = watchdog.WholeRunFailure();

        Assert.NotNull(wholeRun);
        Assert.Contains("probe error", wholeRun, StringComparison.Ordinal);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Peaks_AreHighWaterMarks()
    {
        long gcHeap = 30 * Megabyte;
        var watchdog = new MemoryWatchdog("Fixture", 100 * Megabyte, Metric, () => _peak, () => gcHeap);

        _peak = 80 * Megabyte;
        watchdog.Sample();
        _peak = 10 * Megabyte;
        gcHeap = 5 * Megabyte;
        watchdog.Sample();

        Assert.Equal(80 * Megabyte, watchdog.PeakBytes);
        Assert.Equal(30 * Megabyte, watchdog.GcPeakBytes);
        Assert.Equal("test peak 80 MB, GC heap peak 30 MB, budget 100 MB.", watchdog.Describe());
    }

    private MemoryWatchdog Create(long budgetMegabytes) =>
        new("Fixture", budgetMegabytes * Megabyte, Metric, () => _peak, static () => 0);
}
