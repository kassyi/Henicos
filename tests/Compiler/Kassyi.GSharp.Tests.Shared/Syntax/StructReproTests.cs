using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Reproductions for struct handling gaps. A test that fails today carries the skip reason of its issue.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
public class StructReproTests : TranspilerTestBase
{
    private const string BarrierLocalSameNameSkipReason = "A local live across a barrier with the same name as a struct member is an internal compiler error (#481)";

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_GenericStruct_IntThenFloat()
    {
        var ints = new int[1];
        var floats = new float[1];
        StructReproKernels.LaunchGenericIntFirstKernel(ints, floats, blockDimX: 1);
        Assert.Equal(3, ints[0]);
        Assert.Equal(4.0f, floats[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_GenericStruct_FloatThenInt()
    {
        var ints = new int[1];
        var floats = new float[1];
        StructReproKernels.LaunchGenericFloatFirstKernel(ints, floats, blockDimX: 1);
        Assert.Equal(3, ints[0]);
        Assert.Equal(4.0f, floats[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_GenericStruct_Nested()
    {
        var ints = new int[1];
        var floats = new float[1];
        StructReproKernels.LaunchGenericNestedKernel(ints, floats, blockDimX: 1);
        Assert.Equal(10, ints[0]);
        Assert.Equal(12.0f, floats[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_GenericStruct_ArrayField()
    {
        int[] ints = [0, 3, 4];
        float[] floats = [0f, 1.5f, 2.5f];
        StructReproKernels.LaunchGenericArrayFieldKernel(ints, floats, blockDimX: 1);
        Assert.Equal(7, ints[0]);
        Assert.Equal(4.0f, floats[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StructUsedOnlyAsReturnType()
    {
        var output = new int[1];
        StructReproKernels.LaunchReturnOnlyStructKernel(output, blockDimX: 1);
        Assert.Equal(5, output[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StructReturnedAndWrittenAsLocal()
    {
        var output = new int[1];
        StructReproKernels.LaunchReturnAndLocalStructKernel(output, blockDimX: 1);
        Assert.Equal(5, output[0]);
    }

#if REPRO_A48_INIT
    [SkippableFact(Skip = BarrierLocalSameNameSkipReason)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_LocalAcrossBarrierNamedLikeMember_ObjectInitializer()
    {
        var output = new int[1];
        StructReproKernels.LaunchBarrierLocalSameNameInitializerKernel(output, blockDimX: 1);
        Assert.Equal(1, output[0]);
    }
#endif

#if REPRO_A48_WITH
    [SkippableFact(Skip = BarrierLocalSameNameSkipReason)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_LocalAcrossBarrierNamedLikeMember_With()
    {
        var output = new int[1];
        StructReproKernels.LaunchBarrierLocalSameNameWithKernel(output, blockDimX: 1);
        Assert.Equal(3, output[0]);
    }
#endif

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_LocalAcrossBarrierWithOtherNameThanMember_Control()
    {
        var output = new int[1];
        StructReproKernels.LaunchBarrierLocalDifferentNameKernel(output, blockDimX: 1);
        // W = 0, q.X = 3, p.X = 1
        Assert.Equal(31, output[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_InitAccessorBody_RunsInObjectInitializer()
    {
        var output = new int[2];
        StructReproKernels.LaunchInitBodyKernel(output, blockDimX: 1);
        Assert.Equal([0, 7], output);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SetAndInitAccessorBodies_RunInWithAndAssignment()
    {
        var output = new int[3];
        StructReproKernels.LaunchSetAndWithAccessorBodyKernel(output, blockDimX: 1);
        Assert.Equal([1006, 0, 810], output);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UnsubstitutedTypeParameter_ReportsGsh0003AtMemberType(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(
            "Diagnostics/ComputedPropertyGetterFailure.cu.cs", dialect);

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 42, 17, 42, 33)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "unsubstituted type parameter"));
    }
}
