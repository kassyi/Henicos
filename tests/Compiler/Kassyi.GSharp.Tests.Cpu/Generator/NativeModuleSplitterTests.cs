using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Aot;

namespace Kassyi.GSharp.Tests.Shared.Generator;

[Category(TestCategory.Compiler)]
public class NativeModuleSplitterTests
{
    private const string ModuleStem = "Kassyi.GSharp.Kernels_TestModule";
    private const string Prelude = "// prelude\n";
    private const string DeviceSource = "// device\n";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Split_CpuMultipleUnits_EmitsHeaderAndUnitsWithPreservedKernelOrder()
    {
        var partitionPlan = new NativeModulePlan(
            UnitCount: 4,
            KernelUnitIndices: [2, 0, 2, 3],
            UnitByteCounts: [10L, 0L, 20L, 10L]);
        var includes = new HashSet<string>(StringComparer.Ordinal) { "<cmath>" };
        string[] kernels =
        [
            "void k0() {}",
            "void k1() {}",
            "void k2() {}",
            "void k3() {}"
        ];

        var splitPlan = NativeModuleSplitter.Plan(ModuleStem, isCuda: false, partitionPlan);
        var files = NativeModuleSplitter.Emit(splitPlan, includes, Prelude, DeviceSource, kernels);

        Assert.True(splitPlan.IsSplit);
        Assert.Equal(ModuleStem + ".g.hpp", splitPlan.HeaderFileName);
        Assert.NotNull(files.Header);
        var header = files.Header.Value;
        Assert.Equal(ModuleStem + ".g.hpp", header.FileName);
        Assert.True(header.IncludeErrorSites);
        Assert.Contains("#include <cmath>", header.BodySource, StringComparison.Ordinal);
        Assert.Contains(Prelude, header.BodySource, StringComparison.Ordinal);
        Assert.Contains(DeviceSource, header.BodySource, StringComparison.Ordinal);
        Assert.DoesNotContain("void k0()", header.BodySource, StringComparison.Ordinal);

        Assert.Equal(4, files.Units.Length);
        Assert.Equal(ModuleStem + ".g.cpp", files.Units[0].FileName);
        Assert.Equal(ModuleStem + ".1.g.cpp", files.Units[1].FileName);
        Assert.Equal(ModuleStem + ".2.g.cpp", files.Units[2].FileName);
        Assert.Equal(ModuleStem + ".3.g.cpp", files.Units[3].FileName);

        foreach (var unit in files.Units)
        {
            Assert.False(unit.IncludeErrorSites);
            Assert.StartsWith($"#include \"{ModuleStem}.g.hpp\"", unit.BodySource, StringComparison.Ordinal);
        }

        Assert.Contains("void k1() {}", files.Units[0].BodySource, StringComparison.Ordinal);
        Assert.DoesNotContain("void k", files.Units[1].BodySource, StringComparison.Ordinal);
        var k0Pos = files.Units[2].BodySource.IndexOf("void k0() {}", StringComparison.Ordinal);
        var k2Pos = files.Units[2].BodySource.IndexOf("void k2() {}", StringComparison.Ordinal);
        Assert.True(k0Pos >= 0 && k2Pos > k0Pos, "Kernels in unit 2 must preserve original order");
        Assert.Contains("void k3() {}", files.Units[3].BodySource, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Split_CpuSingleUnit_EmitsSingleFileWithoutHeader()
    {
        var partitionPlan = new NativeModulePlan(
            UnitCount: 1,
            KernelUnitIndices: [0, 0],
            UnitByteCounts: [20L]);
        var includes = new HashSet<string>(StringComparer.Ordinal) { "<cstdint>" };
        string[] kernels = ["void k0() {}", "void k1() {}"];

        var splitPlan = NativeModuleSplitter.Plan(ModuleStem, isCuda: false, partitionPlan);
        var files = NativeModuleSplitter.Emit(splitPlan, includes, Prelude, DeviceSource, kernels);

        Assert.False(splitPlan.IsSplit);
        Assert.Null(splitPlan.HeaderFileName);
        Assert.Null(files.Header);
        var unit = Assert.Single(files.Units);
        Assert.Equal(ModuleStem + ".g.cpp", unit.FileName);
        Assert.True(unit.IncludeErrorSites);

        var expected = NativeModuleAssembler.AssembleBody(
            includes,
            Prelude,
            DeviceSource,
            string.Join(Environment.NewLine, kernels) + Environment.NewLine);
        Assert.Equal(expected, unit.BodySource);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Split_CudaMultipleUnits_EmitsSingleFileWithoutHeader()
    {
        var partitionPlan = new NativeModulePlan(
            UnitCount: 4,
            KernelUnitIndices: [0, 2, 3],
            UnitByteCounts: [10L, 0L, 10L, 10L]);
        var includes = new HashSet<string>(StringComparer.Ordinal) { "<cuda_runtime.h>" };
        string[] kernels = ["__global__ void k0() {}", "__global__ void k1() {}", "__global__ void k2() {}"];

        var splitPlan = NativeModuleSplitter.Plan(ModuleStem, isCuda: true, partitionPlan);
        var files = NativeModuleSplitter.Emit(splitPlan, includes, Prelude, DeviceSource, kernels);

        Assert.False(splitPlan.IsSplit);
        Assert.Null(splitPlan.HeaderFileName);
        Assert.Null(files.Header);
        var unit = Assert.Single(files.Units);
        Assert.Equal(ModuleStem + ".g.cu", unit.FileName);
        Assert.True(unit.IncludeErrorSites);
        Assert.DoesNotContain(".g.hpp", unit.BodySource, StringComparison.Ordinal);
        Assert.Contains("__global__ void k0() {}", unit.BodySource, StringComparison.Ordinal);
        Assert.Contains("__global__ void k1() {}", unit.BodySource, StringComparison.Ordinal);
        Assert.Contains("__global__ void k2() {}", unit.BodySource, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Split_EmptyUnitsIncluded_EmitsAllUnitFilesDeterministically()
    {
        var partitionPlan = new NativeModulePlan(
            UnitCount: 4,
            KernelUnitIndices: [3],
            UnitByteCounts: [0L, 0L, 0L, 10L]);
        var includes = new HashSet<string>(StringComparer.Ordinal);
        string[] kernels = ["void k0() {}"];

        var plan1 = NativeModuleSplitter.Plan(ModuleStem, isCuda: false, partitionPlan);
        var files1 = NativeModuleSplitter.Emit(plan1, includes, Prelude, DeviceSource, kernels);
        var plan2 = NativeModuleSplitter.Plan(ModuleStem, isCuda: false, partitionPlan);
        var files2 = NativeModuleSplitter.Emit(plan2, includes, Prelude, DeviceSource, kernels);

        Assert.Equal(4, files1.Units.Length);
        Assert.Equal(files1.Header, files2.Header);
        Assert.Equal(files1.Units.AsEnumerable(), files2.Units.AsEnumerable());
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal($"#include \"{ModuleStem}.g.hpp\"{Environment.NewLine}", files1.Units[i].BodySource);
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Plan_ZeroUnitCount_ThrowsArgumentOutOfRangeException()
    {
        var invalidPlan = new NativeModulePlan(0, ImmutableArray<int>.Empty, ImmutableArray<long>.Empty);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => NativeModuleSplitter.Plan(ModuleStem, isCuda: false, invalidPlan));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [Scenario(TestScenario.SadPath)]
    public void Plan_OutOfRangeKernelUnitIndex_ThrowsArgumentOutOfRangeException(int invalidUnitIndex)
    {
        var invalidPlan = new NativeModulePlan(
            UnitCount: 4,
            KernelUnitIndices: [0, invalidUnitIndex],
            UnitByteCounts: [10L, 0L, 0L, 0L]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => NativeModuleSplitter.Plan(ModuleStem, isCuda: false, invalidPlan));
    }
}
