using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Aot;

namespace Kassyi.GSharp.Tests.Shared.Generator;

[Category(TestCategory.Compiler)]
public class NativeModulePartitionerTests
{
    private const long OneMib = 1024L * 1024L;

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0L, 1)]
    [InlineData(100L, 1)]
    [InlineData(OneMib - 1, 1)]
    [InlineData(OneMib, 1)]
    [InlineData(OneMib + 1, 2)]
    [InlineData(2L * OneMib - 1, 2)]
    [InlineData(2L * OneMib, 2)]
    [InlineData(2L * OneMib + 1, 4)]
    [InlineData(4L * OneMib - 1, 4)]
    [InlineData(4L * OneMib, 4)]
    [InlineData(4L * OneMib + 1, 8)]
    [InlineData(8L * OneMib - 1, 8)]
    [InlineData(8L * OneMib, 8)]
    [InlineData(8L * OneMib + 1, 16)]
    [InlineData(16L * OneMib - 1, 16)]
    [InlineData(16L * OneMib, 16)]
    [InlineData(16L * OneMib + 1, 32)]
    [InlineData(32L * OneMib - 1, 32)]
    [InlineData(32L * OneMib, 32)]
    [InlineData(32L * OneMib + 1, 32)]
    [InlineData(100L * OneMib, 32)]
    public void ComputeUnitCount_GivenBoundaryByteCount_ReturnsExpectedUnitCount(long totalBytes, int expectedB)
    {
        var actualB = NativeModulePartitioner.ComputeUnitCount(totalBytes);
        Assert.Equal(expectedB, actualB);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GivenTotalBytesUnderOrEqualToOneMib_ReturnsSingleUnitWithZeroIndices()
    {
        var kernels = new[]
        {
            new KernelPartitionItem("global::Namespace.ClassA", 1000),
            new KernelPartitionItem("global::Namespace.ClassB", 2000),
            new KernelPartitionItem("global::Namespace.ClassC", (int)(OneMib - 3000))
        };

        var plan = NativeModulePartitioner.Plan(kernels);

        Assert.Equal(1, plan.UnitCount);
        Assert.Equal(kernels.Length, plan.KernelUnitIndices.Length);
        Assert.All(plan.KernelUnitIndices, index => Assert.Equal(0, index));
        Assert.Single(plan.UnitByteCounts);
        Assert.Equal(OneMib, plan.UnitByteCounts[0]);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GivenEmptyKernels_ReturnsSingleUnitZeroBytes()
    {
        var plan = NativeModulePartitioner.Plan(ReadOnlySpan<KernelPartitionItem>.Empty);

        Assert.Equal(1, plan.UnitCount);
        Assert.Empty(plan.KernelUnitIndices);
        Assert.Single(plan.UnitByteCounts);
        Assert.Equal(0L, plan.UnitByteCounts[0]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_MultipleKernelsInSameClass_AssignedSameUnit()
    {
        // 4 MiB total -> B = 4
        var kernels = new[]
        {
            new KernelPartitionItem("global::Namespace.ClassA", 500_000),
            new KernelPartitionItem("global::Namespace.ClassB", 700_000),
            new KernelPartitionItem("global::Namespace.ClassA", 600_000),
            new KernelPartitionItem("global::Namespace.ClassC", 800_000),
            new KernelPartitionItem("global::Namespace.ClassB", 900_000),
            new KernelPartitionItem("global::Namespace.ClassA", 700_000)
        };

        var plan = NativeModulePartitioner.Plan(kernels);

        Assert.True(plan.UnitCount > 1, "Expected B > 1 for kernels totaling > 2 MiB");
        var unitA0 = plan.KernelUnitIndices[0];
        var unitA2 = plan.KernelUnitIndices[2];
        var unitA5 = plan.KernelUnitIndices[5];
        Assert.Equal(unitA0, unitA2);
        Assert.Equal(unitA0, unitA5);

        var unitB1 = plan.KernelUnitIndices[1];
        var unitB4 = plan.KernelUnitIndices[4];
        Assert.Equal(unitB1, unitB4);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_InputOrderInvariance_PreservesClassUnitAssignment()
    {
        var kernels1 = new[]
        {
            new KernelPartitionItem("global::Namespace.ClassA", 600_000),
            new KernelPartitionItem("global::Namespace.ClassB", 700_000),
            new KernelPartitionItem("global::Namespace.ClassC", 800_000)
        };

        var kernels2 = new[]
        {
            new KernelPartitionItem("global::Namespace.ClassC", 800_000),
            new KernelPartitionItem("global::Namespace.ClassA", 600_000),
            new KernelPartitionItem("global::Namespace.ClassB", 700_000)
        };

        var plan1 = NativeModulePartitioner.Plan(kernels1);
        var plan2 = NativeModulePartitioner.Plan(kernels2);

        Assert.Equal(plan1.UnitCount, plan2.UnitCount);

        // Map class name to unit index in plan 1
        var map1 = new Dictionary<string, int>();
        for (var i = 0; i < kernels1.Length; i++)
        {
            map1[kernels1[i].ClassFullName] = plan1.KernelUnitIndices[i];
        }

        // Verify plan 2 has identical unit index per class
        for (var i = 0; i < kernels2.Length; i++)
        {
            Assert.Equal(map1[kernels2[i].ClassFullName], plan2.KernelUnitIndices[i]);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_ChangingOneClassByteSize_PreservesOtherClassesAssignments()
    {
        // Total stays within 2..4 MiB (B = 4)
        var kernelsOriginal = new[]
        {
            new KernelPartitionItem("global::Namespace.ClassA", 500_000),
            new KernelPartitionItem("global::Namespace.ClassB", 800_000),
            new KernelPartitionItem("global::Namespace.ClassC", 900_000)
        };

        var kernelsModified = new[]
        {
            new KernelPartitionItem("global::Namespace.ClassA", 900_000), // changed from 500k to 900k
            new KernelPartitionItem("global::Namespace.ClassB", 800_000),
            new KernelPartitionItem("global::Namespace.ClassC", 900_000)
        };

        var planOriginal = NativeModulePartitioner.Plan(kernelsOriginal);
        var planModified = NativeModulePartitioner.Plan(kernelsModified);

        Assert.Equal(planOriginal.UnitCount, planModified.UnitCount);
        // ClassB and ClassC unit indices remain unchanged
        Assert.Equal(planOriginal.KernelUnitIndices[1], planModified.KernelUnitIndices[1]);
        Assert.Equal(planOriginal.KernelUnitIndices[2], planModified.KernelUnitIndices[2]);
        // ClassA unit index also remains unchanged since hash depends on name
        Assert.Equal(planOriginal.KernelUnitIndices[0], planModified.KernelUnitIndices[0]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_UnitByteCounts_EqualsSumOfKernelsInEachUnit()
    {
        var kernels = new[]
        {
            new KernelPartitionItem("global::Namespace.ClassA", 600_000),
            new KernelPartitionItem("global::Namespace.ClassB", 700_000),
            new KernelPartitionItem("global::Namespace.ClassC", 800_000),
            new KernelPartitionItem("global::Namespace.ClassD", 900_000)
        };

        var plan = NativeModulePartitioner.Plan(kernels);
        var expectedUnitBytes = new long[plan.UnitCount];
        for (var i = 0; i < kernels.Length; i++)
        {
            expectedUnitBytes[plan.KernelUnitIndices[i]] += kernels[i].ByteCount;
        }

        Assert.Equal(expectedUnitBytes, plan.UnitByteCounts.ToArray());
        long totalKernels = 600_000 + 700_000 + 800_000 + 900_000;
        Assert.Equal(totalKernels, plan.UnitByteCounts.Sum());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void FormatPartitionJson_ProducesExpectedJsonStructure()
    {
        var kernels = new[]
        {
            new KernelPartitionItem("global::Namespace.ZClass", 1_200_000),
            new KernelPartitionItem("global::Namespace.AClass", 1_300_000)
        };

        var plan = NativeModulePartitioner.Plan(kernels);
        var json = AotArtifactWriter.FormatPartitionJson("Cpu", plan, kernels);

        Assert.Contains("\"dialect\": \"Cpu\"", json, StringComparison.Ordinal);
        Assert.Contains("\"B\": 4", json, StringComparison.Ordinal);
        Assert.Contains("\"units\": [", json, StringComparison.Ordinal);
        Assert.Contains("\"classes\": [", json, StringComparison.Ordinal);
        Assert.Contains("\"class\": \"global::Namespace.AClass\"", json, StringComparison.Ordinal);
        Assert.Contains("\"class\": \"global::Namespace.ZClass\"", json, StringComparison.Ordinal);

        // Verify classes are sorted alphabetically by class name
        var indexOfA = json.IndexOf("global::Namespace.AClass", StringComparison.Ordinal);
        var indexOfZ = json.IndexOf("global::Namespace.ZClass", StringComparison.Ordinal);
        Assert.True(indexOfA < indexOfZ, "Classes in partition JSON must be sorted alphabetically by class name");
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("ClassA", 4, 2)]
    [InlineData("ClassB", 2, 1)]
    [InlineData("ClassB", 4, 3)]
    [InlineData("ClassC", 4, 0)]
    [InlineData("ClassD", 4, 1)]
    public void ComputeUnitIndex_GivenKnownClass_UsesModuloBNotModuloBMinusOne(
        string className, int unitCount, int expectedUnit)
    {
        var actualUnit = NativeModulePartitioner.ComputeUnitIndex(className, unitCount);
        Assert.Equal(expectedUnit, actualUnit);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ComputeClassHash_Utf8AndAscii_ProduceDeterministicHash()
    {
        var hashAscii = NativeModulePartitioner.ComputeClassHash("global::MyCompany.MyProduct.KernelClass");
        Assert.NotEqual(0UL, hashAscii);

        // Deterministic across repeated calls
        var hashRepeat = NativeModulePartitioner.ComputeClassHash("global::MyCompany.MyProduct.KernelClass");
        Assert.Equal(hashAscii, hashRepeat);

        // Supports non-ASCII UTF-8 characters without throwing
        var hashUtf8 = NativeModulePartitioner.ComputeClassHash("global::テスト名前空間.カーネルクラス");
        Assert.NotEqual(0UL, hashUtf8);
        Assert.Equal(hashUtf8, NativeModulePartitioner.ComputeClassHash("global::テスト名前空間.カーネルクラス"));
    }
}
