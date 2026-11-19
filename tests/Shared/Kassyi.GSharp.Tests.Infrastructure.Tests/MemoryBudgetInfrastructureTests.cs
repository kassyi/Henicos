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
        Assert.Equal(
            names.Order(StringComparer.Ordinal),
            ordered.Select(static c => c.DisplayName).Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void LinuxStatus_PrivateResidentIsAnonymousPlusSharedInBytes()
    {
        // The mapped files (RssFile) and the kernel's peaks (VmHWM, VmPeak) are not counted
        const string Status =
            "Name:\ttesthost\nVmPeak:\t 9999999 kB\nVmHWM:\t  123456 kB\nVmRSS:\t  100000 kB\n"
            + "RssAnon:\t   30000 kB\nRssFile:\t   60000 kB\nRssShmem:\t   10000 kB\n";

        long bytes = ProcessMemory.ReadLinuxPrivateResident(Status);

        Assert.Equal((30000L + 10000L) * 1024, bytes);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Name:\ttesthost\nRssShmem:\t 1 kB\n")]
    [InlineData("Name:\ttesthost\nRssAnon:\t 1 kB\n")]
    [InlineData("Name:\ttesthost\nRssAnon:\t x kB\nRssShmem:\t 1 kB\n")]
    public void LinuxStatus_WithoutAValidField_Throws(string status)
    {
        Assert.Throws<IOException>(() => ProcessMemory.ReadLinuxPrivateResident(status));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ReadBytes_CountsTouchedNativeMemory()
    {
        const long Bytes = 64L * 1024 * 1024;
        long before = ProcessMemory.ReadBytes();

        IntPtr block = Marshal.AllocHGlobal((nint)(before + Bytes));
        try
        {
            for (long offset = 0; offset < before + Bytes; offset += 4096)
            {
                Marshal.WriteByte((nint)block + (nint)offset, 1);
            }

            long after = ProcessMemory.ReadBytes();

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
    public void AvailablePhysicalMemory_DescribesTheMachine_NotTheHeapHardLimit()
    {
        long available = ProcessMemory.ReadAvailablePhysicalBytes();
        GCMemoryInfo info = GC.GetGCMemoryInfo();

        // Under the heap hard limit of this host, TotalAvailableMemoryBytes is the limit, so the GC's view of free
        // memory (limit minus the machine's load) is unrelated to the machine; the operating system's value is not
        Assert.True(available > 0, $"available {available}");
        Assert.NotEqual(info.TotalAvailableMemoryBytes - info.MemoryLoadBytes, available);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void HeapHardLimit_IsTwiceTheLargerBudgetOfTheFile()
    {
        MemoryBudgetEntry entry = MemoryBudgetFile.Parse(File.ReadAllText(MemoryBudgetFile.FullPath))
            .Single(static e => e.Name == "Kassyi.GSharp.Tests.Infrastructure.Tests");
        long budgetMegabytes = Math.Max(entry.Megabytes, entry.StressMegabytes);

        // TotalAvailableMemoryBytes is the heap hard limit when one is set (tests/Directory.Build.props)
        long limit = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

        Assert.Equal(2 * budgetMegabytes * 1024 * 1024, limit);
    }
}
