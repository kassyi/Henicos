namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A struct whose host-only members reach a barrier still builds for the device and its kernel runs
/// (<see cref="StructHostOnlyMemberKernels"/>).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class StructHostOnlyMemberTests
{
    private const int Count = 4;

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void HostOnlyMembersKernel_MembersCalledOnlyByTheHost_RunsTheKernel()
    {
        var output = new int[Count];

        StructHostOnlyMemberKernels.LaunchHostOnlyMembersKernel(output, blockDimX: Count);

        Assert.Equal([1, 2, 3, 4], output);
    }
}
