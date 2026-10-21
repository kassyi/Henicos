using Kassyi.GSharp.Tests.Shared.Syntax.GlobalNames;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Verifies that types whose C# paths used to share one C++ name stay distinct on the device.
/// </summary>
/// <remarks>
/// Same-named <c>file</c> types are covered by <c>GlobalCppNamesTests</c> only: launching a kernel that uses a
/// <c>file</c> type does not build on the managed path yet (#427).
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
public class GlobalNameCollisionTests : TranspilerTestBase
{
    private const int ThreadCount = 4;

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_UnderscoreInTypeName_DoesNotMergeWithNestedType()
    {
        var output = new int[ThreadCount];

        GlobalNameCollisionKernels.LaunchUnderscoreInNameKernel(output, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(1240 + i, output[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_NestedTypeAndUnderscoreType_AreDistinct()
    {
        var output = new int[ThreadCount];

        GlobalNameCollisionKernels.LaunchNestedTypeAndUnderscoreKernel(output, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(730 + i, output[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_GenericTypesOfDifferentArity_AreDistinct()
    {
        var output = new int[ThreadCount];

        GlobalNameCollisionKernels.LaunchGenericArityKernel(output, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(567 + i, output[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_SameNameDeviceFunctionsInDifferentTypes_CallTheBoundFunction()
    {
        var output = new int[ThreadCount];

        GlobalNameCollisionKernels.LaunchSameNameDeviceFunctionsKernel(output, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            int value = i + 1;
            Assert.Equal(value * 10 + value * 100 + value * 200 * 10000, output[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_OverloadsWithinOneType_FollowCsharpOverloadResolution()
    {
        var output = new int[ThreadCount];

        GlobalNameCollisionKernels.LaunchOverloadResolutionKernel(output, blockDimX: ThreadCount);

        Assert.All(output, value => Assert.Equal(21, value));
    }
}
