#nullable enable
using System.Linq;
using System.Runtime.InteropServices;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Kassyi.GSharp.Tests.Infrastructure.Tests;

/// <summary>
/// Pins the collection order that makes the final memory budget check run last, and the process memory reader.
/// </summary>
public class MemoryBudgetInfrastructureTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void CollectionOrderer_PutsTheFinalCollectionLast()
    {
        var assembly = new TestAssembly(Reflector.Wrap(typeof(MemoryBudgetInfrastructureTests).Assembly));
        string[] names = ["Alpha", MemoryBudget.FinalCollection, TestCollections.DeviceExclusive, "Omega", "Beta"];
        var collections = names
            .Select(n => (ITestCollection)new TestCollection(assembly, null, n, Guid.NewGuid()))
            .ToList();

        var ordered = new MemoryBudgetCollectionOrderer().OrderTestCollections(collections).ToList();

        Assert.Equal(names.Length, ordered.Count);
        Assert.Equal(MemoryBudget.FinalCollection, ordered[^1].DisplayName);
        Assert.Equal(names.Order(StringComparer.Ordinal), ordered.Select(static c => c.DisplayName).Order(StringComparer.Ordinal));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void LinuxStatus_PeakResidentIsReadInBytes()
    {
        const string Status = "Name:\ttesthost\nVmPeak:\t 9999999 kB\nVmHWM:\t  123456 kB\nVmRSS:\t  100000 kB\n";

        long peak = ProcessMemory.ReadLinuxPeakResident(Status);

        Assert.Equal(123456L * 1024, peak);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void LinuxStatus_WithoutPeakLine_Throws()
    {
        Assert.Throws<IOException>(static () => ProcessMemory.ReadLinuxPeakResident("Name:\ttesthost\nVmRSS:\t 1 kB\n"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ReadPeakBytes_CountsTouchedNativeMemory()
    {
        const long Bytes = 64L * 1024 * 1024;
        long before = ProcessMemory.ReadPeakBytes();

        IntPtr block = Marshal.AllocHGlobal((nint)(before + Bytes));
        try
        {
            for (long offset = 0; offset < before + Bytes; offset += 4096)
            {
                Marshal.WriteByte((nint)block + (nint)offset, 1);
            }

            long after = ProcessMemory.ReadPeakBytes();

            Assert.True(after >= before + Bytes, $"peak before {before}, after {after}");
        }
        finally
        {
            Marshal.FreeHGlobal(block);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ModuleInitializer_StartedTheWatchdogWithThisAssemblysBudget()
    {
        MemoryWatchdog? watchdog = MemoryBudget.Current;

        Assert.NotNull(watchdog);
        Assert.True(watchdog.BudgetBytes > 0, watchdog.Describe());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void HeapHardLimit_IsTwiceTheBudgetOfTheFile()
    {
        long budgetMegabytes = MemoryBudgetFile.Parse(File.ReadAllText(MemoryBudgetFile.FullPath))
            .Single(static e => e.Key == "Kassyi.GSharp.Tests.Infrastructure.Tests")
            .Value;

        // TotalAvailableMemoryBytes is the heap hard limit when one is set (tests/Directory.Build.props)
        long limit = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

        Assert.Equal(2 * budgetMegabytes * 1024 * 1024, limit);
    }
}
