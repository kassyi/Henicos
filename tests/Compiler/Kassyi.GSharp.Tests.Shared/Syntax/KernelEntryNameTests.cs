using Kassyi.GSharp.Tests.Shared.Syntax.GlobalNames;
namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Verifies that kernel entry points whose C# paths used to share one exported name, and overloaded kernels, each run
/// their own body.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
public partial class KernelEntryNameTests : TranspilerTestBase
{
    internal const int ThreadCount = 4;

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Launch_EntryPointsWithUnderscoreAcrossTypeAndMethod_KeepsEntryPointsDistinct()
    {
        var first = new int[ThreadCount];
        var second = new int[ThreadCount];

        Entry_Host.LaunchRunKernel(first, blockDimX: ThreadCount);
        Entry.LaunchHost_RunKernel(second, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(100 + i, first[i]);
            Assert.Equal(200 + i, second[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Launch_SameNamedKernelClassesInDifferentNamespaces_KeepsNamespacesDistinct()
    {
        var first = new int[ThreadCount];
        var second = new int[ThreadCount];

        GlobalNames.First.SameNamedKernels.LaunchRunKernel(first, blockDimX: ThreadCount);
        GlobalNames.Second.SameNamedKernels.LaunchRunKernel(second, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(300 + i, first[i]);
            Assert.Equal(400 + i, second[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Launch_OverloadedKernelsWithArrayBuffers_RunsSelectedOverload()
    {
        var ints = new int[ThreadCount];
        var floats = new float[ThreadCount];

        OverloadedKernels.LaunchFillKernel(ints, blockDimX: ThreadCount);
        OverloadedKernels.LaunchFillKernel(floats, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(i * 2, ints[i]);
            Assert.Equal(i * 0.5f, floats[i]);
        }
    }
}
