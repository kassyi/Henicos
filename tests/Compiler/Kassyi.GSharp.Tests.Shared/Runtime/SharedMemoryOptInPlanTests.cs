#nullable enable
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// <see cref="SharedMemoryOptInPlan"/>: what the CUDA path does for a launch's dynamic shared memory (#799). The
/// values are those of a compute capability 10.7 GPU (227 KB opt-in, 327 KB oversized per block) unless a test says
/// otherwise. No GPU is used.
/// </summary>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class SharedMemoryOptInPlanTests
{
    private const int DefaultDynamicBytes = 48 * 1024;
    private const int OptInBytes = 232448;
    private const int OversizedBytes = 334848;

    private static SharedMemoryOptInPlan Decide(uint requested, bool allowsOversized, int staticBytes = 0,
        int allowedDynamicBytes = DefaultDynamicBytes, int oversizedBytes = OversizedBytes, bool modeIsSet = false) =>
        SharedMemoryOptInPlan.Decide(requested, allowsOversized,
            new SharedMemoryFunctionState(staticBytes, allowedDynamicBytes, OptInBytes, oversizedBytes, modeIsSet));

    private static void AssertAction(SharedMemoryOptInAction expected, SharedMemoryOptInPlan plan) =>
        Assert.Equal(expected, plan.Action);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Decide_NoDynamicSharedMemory_DoesNothing(bool allowsOversized)
    {
        AssertAction(SharedMemoryOptInAction.None, Decide(0, allowsOversized, allowedDynamicBytes: 0));
    }

    // A request the function already allows makes no driver call, also when the opt-in limit is higher
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1000u, false)]
    [InlineData(1000u, true)]
    [InlineData((uint)DefaultDynamicBytes, true)]
    public void Decide_RequestWithinTheAllowedBytes_DoesNothing(uint requested, bool allowsOversized)
    {
        AssertAction(SharedMemoryOptInAction.None, Decide(requested, allowsOversized));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Decide_OneByteAboveTheAllowedBytes_RaisesTheDynamicLimit(bool allowsOversized)
    {
        AssertAction(SharedMemoryOptInAction.RaiseDynamicLimit, Decide(DefaultDynamicBytes + 1, allowsOversized));
    }

    // A marked kernel whose request fits the opt-in limit makes the same driver call as an unmarked one
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Decide_RequestAtTheOptInLimit_RaisesTheDynamicLimitWithoutTheMode(bool allowsOversized)
    {
        AssertAction(SharedMemoryOptInAction.RaiseDynamicLimit, Decide(OptInBytes, allowsOversized));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(OptInBytes + 1u)]
    [InlineData(300000u)]
    [InlineData((uint)OversizedBytes)]
    public void Decide_MarkedRequestAboveTheOptInLimitWithinTheOversizedLimit_AllowsOversized(uint requested)
    {
        AssertAction(SharedMemoryOptInAction.AllowOversized, Decide(requested, allowsOversized: true));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Decide_MarkedRequestAboveTheOversizedLimit_RejectsWithTheOversizedLimit()
    {
        var plan = Decide(OversizedBytes + 1, allowsOversized: true);

        Assert.Equal(new SharedMemoryOptInPlan(SharedMemoryOptInAction.Reject, OversizedBytes, 0), plan);
    }

    // The unmarked kernel's message names the property that would let the launch through
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Decide_UnmarkedRequestOneByteAboveTheOptInLimit_RejectsWithTheOptInLimitAndTheOversizedHint()
    {
        var plan = Decide(OptInBytes + 1, allowsOversized: false);

        Assert.Equal(new SharedMemoryOptInPlan(SharedMemoryOptInAction.Reject, OptInBytes, OversizedBytes), plan);
        var message = DynamicSharedMemoryOptIn.FormatRejection(OptInBytes + 1, 0, plan, 10, 7, "Kernels.Big");
        Assert.Equal(
            "G#: sharedMemBytes 232449 plus 0 bytes of static shared memory exceeds the 232448 bytes of shared memory " +
            "per block of this GPU (sm_107); this GPU holds up to 334848 bytes for a kernel with " +
            "[GpuKernel(AllowOversizedSharedMemory = true)] (Kernels.Big).", message);
    }

    // Above the oversized limit the property would not help either
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Decide_UnmarkedRequestAboveTheOversizedLimit_RejectsWithoutTheHint()
    {
        var plan = Decide(OversizedBytes + 1, allowsOversized: false);

        Assert.Equal(new SharedMemoryOptInPlan(SharedMemoryOptInAction.Reject, OptInBytes, 0), plan);
        var message = DynamicSharedMemoryOptIn.FormatRejection(OversizedBytes + 1, 0, plan, 10, 7, "Kernels.Big");
        Assert.Equal(
            "G#: sharedMemBytes 334849 plus 0 bytes of static shared memory exceeds the 232448 bytes of shared memory " +
            "per block of this GPU (sm_107) (Kernels.Big).", message);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(OptInBytes - 4096u, nameof(SharedMemoryOptInAction.RaiseDynamicLimit))]
    [InlineData(OptInBytes - 4095u, nameof(SharedMemoryOptInAction.AllowOversized))]
    [InlineData(OversizedBytes - 4096u, nameof(SharedMemoryOptInAction.AllowOversized))]
    [InlineData(OversizedBytes - 4095u, nameof(SharedMemoryOptInAction.Reject))]
    public void Decide_MarkedKernelWithStaticSharedMemory_CountsStaticPlusDynamic(uint requested, string expected)
    {
        Assert.Equal(expected, Decide(requested, allowsOversized: true, staticBytes: 4096).Action.ToString());
    }

    // Static shared memory above the opt-in limit leaves no dynamic bytes; the subtraction must not go negative
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Decide_StaticSharedMemoryAboveTheOptInLimit_RejectsAnyDynamicRequest()
    {
        AssertAction(SharedMemoryOptInAction.Reject,
            Decide(1, allowsOversized: false, staticBytes: OptInBytes + 1, allowedDynamicBytes: 0));
    }

    // 0 is also what the runtime passes when the query of the device attribute fails
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(OptInBytes)]
    [InlineData(DefaultDynamicBytes)]
    public void Decide_MarkedWithoutAnOversizedLimitAboveTheOptInLimit_BehavesAsUnmarked(int oversizedBytes)
    {
        foreach (var requested in new uint[] { 1000, DefaultDynamicBytes + 1, OptInBytes, OptInBytes + 1, OversizedBytes })
        {
            var marked = Decide(requested, allowsOversized: true, oversizedBytes: oversizedBytes);
            var unmarked = Decide(requested, allowsOversized: false, oversizedBytes: 0);
            Assert.Equal(unmarked, marked);
        }

        Assert.Equal(new SharedMemoryOptInPlan(SharedMemoryOptInAction.Reject, OptInBytes, 0),
            Decide(OptInBytes + 1, allowsOversized: true, oversizedBytes: oversizedBytes));
    }

    // An oversized limit that is not above the opt-in limit gives no hint to an unmarked kernel either
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Decide_UnmarkedWithAnOversizedLimitBelowTheOptInLimit_RejectsWithoutTheHint()
    {
        var plan = Decide(OptInBytes + 1, allowsOversized: false, oversizedBytes: DefaultDynamicBytes);

        Assert.Equal(new SharedMemoryOptInPlan(SharedMemoryOptInAction.Reject, OptInBytes, 0), plan);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1000u)]
    [InlineData(100000u)]
    [InlineData((uint)OptInBytes)]
    [InlineData((uint)OversizedBytes)]
    public void Decide_ModeAlreadySet_DoesNothingUpToTheOversizedLimit(uint requested)
    {
        AssertAction(SharedMemoryOptInAction.None,
            Decide(requested, allowsOversized: true, modeIsSet: true));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Decide_ModeAlreadySetAndRequestAboveTheOversizedLimit_Rejects()
    {
        Assert.Equal(new SharedMemoryOptInPlan(SharedMemoryOptInAction.Reject, OversizedBytes, 0),
            Decide(OversizedBytes + 1, allowsOversized: true, modeIsSet: true));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Decide_RequestAtMaxValue_RejectsWithoutOverflow(bool allowsOversized)
    {
        AssertAction(SharedMemoryOptInAction.Reject, Decide(uint.MaxValue, allowsOversized));
    }

    // The message of an unmarked kernel on a GPU without an oversized limit is the one of #545
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void FormatRejection_WithoutHint_KeepsTheExistingMessage()
    {
        var plan = new SharedMemoryOptInPlan(SharedMemoryOptInAction.Reject, 101376, 0);

        Assert.Equal(
            "G#: sharedMemBytes 101377 plus 16 bytes of static shared memory exceeds the 101376 bytes of shared memory " +
            "per block of this GPU (sm_86) (Kernels.Small).",
            DynamicSharedMemoryOptIn.FormatRejection(101377, 16, plan, 8, 6, "Kernels.Small"));
    }
}
