#nullable enable

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed unsafe class SplitUnitErrorChannelTests
{
    private const string ModuleStem =
        "Kassyi.GSharp.Kernels_Kassyi_x00002eGSharp_x00002eTests_x00002eCpu";
    private const string PartitionFileName = ModuleStem + ".partition.json";
    private const string PrimaryClassFullName =
        "global::Kassyi.GSharp.Tests.Cpu.Runtime.SplitUnitErrorChannelKernels";
    private const string CompanionClassFullName =
        "global::Kassyi.GSharp.Tests.Cpu.Runtime.SplitUnitCompanionKernels";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PartitionPlan_CpuAssembly_HasMultipleUnitsAndNonZeroUnitClass()
    {
        EnsureNativeLibraryPresent();
        var plan = ReadPartitionPlan();

        Assert.Equal("Cpu", plan.Dialect);
        Assert.True(plan.UnitCount > 1, $"Expected split CPU module (B > 1), found B = {plan.UnitCount}.");
        Assert.True(plan.ClassUnits.TryGetValue(PrimaryClassFullName, out var primaryUnit));
        Assert.True(plan.ClassUnits.TryGetValue(CompanionClassFullName, out var companionUnit));
        Assert.True(
            primaryUnit != 0 || companionUnit != 0,
            $"Expected at least one kernel class in unit != 0, got {primaryUnit} and {companionUnit}.");
        Assert.NotEqual(primaryUnit, companionUnit);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void NonUnitZeroKernel_ElementAtOutOfRange_ThrowsWithSourceLocationAndMemberName()
    {
        EnsureNativeLibraryPresent();
        var plan = ReadPartitionPlan();
        Assert.True(plan.UnitCount > 1, $"Expected split CPU module (B > 1), found B = {plan.UnitCount}.");

        // Resolve from the active partition plan which class sits in unit != 0 instead of hardcoding a unit index.
        Assert.True(plan.ClassUnits.TryGetValue(PrimaryClassFullName, out var primaryUnit));
        Assert.True(plan.ClassUnits.TryGetValue(CompanionClassFullName, out var companionUnit));

        int[] data = [10, 20, 30];
        int[] result = [0];
        string expectedMember;
        ArgumentOutOfRangeException ex;

        if (primaryUnit != 0)
        {
            expectedMember = nameof(SplitUnitErrorChannelKernels.OutOfRangeElementAtKernel);
            ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
                SplitUnitErrorChannelKernels.LaunchOutOfRangeElementAtKernel(data, 999, result, blockDimX: 1));
        }
        else
        {
            Assert.True(companionUnit != 0, "Expected at least one test kernel class in unit != 0.");
            expectedMember = nameof(SplitUnitCompanionKernels.CompanionElementAtTrapKernel);
            ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
                SplitUnitCompanionKernels.LaunchCompanionElementAtTrapKernel(data, 999, result, blockDimX: 1));
        }

        Assert.Contains("Index was out of range.", ex.Message, StringComparison.Ordinal);
        Assert.Contains(
            $"in {expectedMember} at SplitUnitErrorChannelKernels.cu.cs",
            ex.Message,
            StringComparison.Ordinal);
        Assert.Contains("in thread (0,0,0) of block (0,0,0)", ex.Message, StringComparison.Ordinal);
        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal(expectedMember, ex.Data["GSharp.MemberName"]);
        var sourceLocation = ex.Data["GSharp.SourceLocation"]?.ToString();
        Assert.NotNull(sourceLocation);
        Assert.StartsWith("SplitUnitErrorChannelKernels.cu.cs(", sourceLocation, StringComparison.Ordinal);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void NonUnitZeroKernel_PointerLauncherOutOfRange_ThrowsWithSourceLocationAndMemberName()
    {
        EnsureNativeLibraryPresent();
        var plan = ReadPartitionPlan();
        Assert.True(plan.UnitCount > 1, $"Expected split CPU module (B > 1), found B = {plan.UnitCount}.");
        Assert.True(plan.ClassUnits.TryGetValue(PrimaryClassFullName, out var primaryUnit));
        Assert.True(plan.ClassUnits.TryGetValue(CompanionClassFullName, out var companionUnit));
        Assert.True(
            primaryUnit != 0 || companionUnit != 0,
            $"Expected at least one kernel class in unit != 0, got {primaryUnit} and {companionUnit}.");
        // SplitUnitErrorChannelKernels has an odd FNV-1a 64 hash (0x326289034943cd3b), so primaryUnit > 0 for 2^k B.
        Assert.InRange(primaryUnit, 1, plan.UnitCount - 1);

        int[] keys = [0, 2];
        int[] result = new int[keys.Length];

        var ex = Assert.Throws<SwitchExpressionException>(() =>
        {
            fixed (int* keysPtr = keys)
            fixed (int* resultPtr = result)
            {
                SplitUnitErrorChannelKernels.LaunchSwitchExpressionArmKernelPtr(
                    Proxy(keysPtr, keys.Length),
                    Proxy(resultPtr, result.Length),
                    blockDimX: 2);
            }
        });

        Assert.Contains(
            "Non-exhaustive switch expression failed to match its input.",
            ex.Message,
            StringComparison.Ordinal);
        Assert.Equal(nameof(GSharpErrorCode.SwitchExpression), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal(nameof(SplitUnitErrorChannelKernels.SwitchExpressionArmKernel), ex.Data["GSharp.MemberName"]);
        var sourceLocation = ex.Data["GSharp.SourceLocation"]?.ToString();
        Assert.NotNull(sourceLocation);
        Assert.StartsWith("SplitUnitErrorChannelKernels.cu.cs(", sourceLocation, StringComparison.Ordinal);
        Assert.Equal("(1,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NonUnitZeroKernel_ConsecutiveCrossUnitLaunches_IsolatesErrorSlotAndCoordinates()
    {
        EnsureNativeLibraryPresent();
        var plan = ReadPartitionPlan();
        Assert.True(plan.UnitCount > 1, $"Expected split CPU module (B > 1), found B = {plan.UnitCount}.");
        Assert.True(plan.ClassUnits.TryGetValue(PrimaryClassFullName, out var primaryUnit));
        Assert.True(plan.ClassUnits.TryGetValue(CompanionClassFullName, out var companionUnit));
        Assert.True(
            primaryUnit != 0 || companionUnit != 0,
            $"Expected at least one kernel class in unit != 0, got {primaryUnit} and {companionUnit}.");
        Assert.NotEqual(primaryUnit, companionUnit);

        int[] input = [1, 2, 3, 4, 5, 6, 7, 8];
        int[] firstOutput = new int[input.Length];
        int[] secondOutput = new int[input.Length];
        int[] data = [10, 20, 30];
        int[] trapResult = [0];

        if (primaryUnit != 0)
        {
            RunCrossUnitSequence(
                firstHealthy: () =>
                {
                    SplitUnitErrorChannelKernels.LaunchHealthyComputeKernel(
                        input,
                        firstOutput,
                        gridDimX: 2,
                        blockDimX: 4);
                    Assert.Equal([101, 102, 103, 104, 105, 106, 107, 108], firstOutput);
                },
                firstTrap: () =>
                    SplitUnitErrorChannelKernels.LaunchOutOfRangeElementAtKernel(data, 999, trapResult, blockDimX: 1),
                firstMember: nameof(SplitUnitErrorChannelKernels.OutOfRangeElementAtKernel),
                secondHealthy: () =>
                {
                    SplitUnitCompanionKernels.LaunchHealthyCompanionKernel(
                        input,
                        secondOutput,
                        gridDimX: 2,
                        blockDimX: 4);
                    Assert.Equal([4, 7, 10, 13, 16, 19, 22, 25], secondOutput);
                },
                secondTrap: () =>
                    SplitUnitCompanionKernels.LaunchCompanionElementAtTrapKernel(data, 999, trapResult, blockDimX: 1),
                secondMember: nameof(SplitUnitCompanionKernels.CompanionElementAtTrapKernel));
        }
        else
        {
            RunCrossUnitSequence(
                firstHealthy: () =>
                {
                    SplitUnitCompanionKernels.LaunchHealthyCompanionKernel(
                        input,
                        firstOutput,
                        gridDimX: 2,
                        blockDimX: 4);
                    Assert.Equal([4, 7, 10, 13, 16, 19, 22, 25], firstOutput);
                },
                firstTrap: () =>
                    SplitUnitCompanionKernels.LaunchCompanionElementAtTrapKernel(data, 999, trapResult, blockDimX: 1),
                firstMember: nameof(SplitUnitCompanionKernels.CompanionElementAtTrapKernel),
                secondHealthy: () =>
                {
                    SplitUnitErrorChannelKernels.LaunchHealthyComputeKernel(
                        input,
                        secondOutput,
                        gridDimX: 2,
                        blockDimX: 4);
                    Assert.Equal([101, 102, 103, 104, 105, 106, 107, 108], secondOutput);
                },
                secondTrap: () =>
                    SplitUnitErrorChannelKernels.LaunchOutOfRangeElementAtKernel(data, 999, trapResult, blockDimX: 1),
                secondMember: nameof(SplitUnitErrorChannelKernels.OutOfRangeElementAtKernel));
        }
    }

    private static void RunCrossUnitSequence(
        Action firstHealthy,
        Action firstTrap,
        string firstMember,
        Action secondHealthy,
        Action secondTrap,
        string secondMember)
    {
        firstHealthy();

        var firstEx = Assert.Throws<ArgumentOutOfRangeException>(firstTrap);
        Assert.Equal(firstMember, firstEx.Data["GSharp.MemberName"]);

        secondHealthy();

        var secondEx = Assert.Throws<ArgumentOutOfRangeException>(secondTrap);
        Assert.Equal(secondMember, secondEx.Data["GSharp.MemberName"]);
        var secondLocation = secondEx.Data["GSharp.SourceLocation"]?.ToString();
        Assert.NotNull(secondLocation);
        Assert.StartsWith("SplitUnitErrorChannelKernels.cu.cs(", secondLocation, StringComparison.Ordinal);
    }

    private static GSharpArrayProxy Proxy<T>(T* data, int length) where T : unmanaged =>
        new() { Data = (IntPtr)data, Length = length };

    private static void EnsureNativeLibraryPresent()
    {
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var dllPath = Path.Combine(AppContext.BaseDirectory, $"{ModuleStem}_{arch}.dll");
        Assert.True(
            File.Exists(dllPath),
            $"Native CPU library '{dllPath}' must exist so tests verify native execution " +
            "rather than managed fallback.");
    }

    private static PartitionPlanSnapshot ReadPartitionPlan()
    {
        var partitionPath = LocatePartitionJsonPath();
        using var stream = File.OpenRead(partitionPath);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var dialect = root.GetProperty("dialect").GetString() ?? string.Empty;
        var unitCount = root.GetProperty("B").GetInt32();
        var classUnits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in root.GetProperty("classes").EnumerateArray())
        {
            var className = entry.GetProperty("class").GetString();
            if (!string.IsNullOrEmpty(className))
            {
                classUnits[className] = entry.GetProperty("unit").GetInt32();
            }
        }

        return new PartitionPlanSnapshot(dialect, unitCount, classUnits);
    }

    private static string LocatePartitionJsonPath()
    {
        var projectDir = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Cpu");
        var relativeFromBin = Path.GetRelativePath(Path.Combine(projectDir, "bin"), AppContext.BaseDirectory);
        var candidate = Path.Combine(projectDir, "obj", relativeFromBin, PartitionFileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var objDir = Path.Combine(projectDir, "obj");
        Assert.True(Directory.Exists(objDir), $"Intermediate directory '{objDir}' was not found.");

        string? newestPath = null;
        var newestWriteTime = DateTime.MinValue;
        foreach (var file in Directory.EnumerateFiles(objDir, PartitionFileName, SearchOption.AllDirectories))
        {
            var writeTime = File.GetLastWriteTimeUtc(file);
            if (newestPath is null || writeTime > newestWriteTime)
            {
                newestPath = file;
                newestWriteTime = writeTime;
            }
        }

        Assert.True(
            newestPath is not null && File.Exists(newestPath),
            $"Could not locate '{PartitionFileName}' under '{objDir}'.");
        return newestPath;
    }

    private sealed record PartitionPlanSnapshot(
        string Dialect,
        int UnitCount,
        IReadOnlyDictionary<string, int> ClassUnits);
}
