using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Execution tests for a declaration whose declarators are partly moved into the context of the CPU state machine and
/// partly stay locals (#1257): every declarator runs in the order written.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class MixedHoistDeclarationTests : TranspilerTestBase
{
    internal const int BlockSize = 32;

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_HoistedBeforeLocalKernel_LaterDeclaratorReadsHoistedValue() =>
        AssertHoistedBeforeLocal();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SideEffectOrderKernel_DeclaratorsRunInSourceOrder() =>
        AssertSideEffectOrder();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_AlternatingHoistKernel_DeclaratorsRunInSourceOrder() =>
        AssertAlternatingHoist();

    internal static void AssertHoistedBeforeLocal()
    {
        var o = new int[BlockSize];
        var p = new int[BlockSize];
        MixedHoistDeclarationKernels.LaunchHoistedBeforeLocalKernel(o, p, 1, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(t + 1, p[t]);
            Assert.Equal(7, o[t]);
        }
    }

    internal static void AssertSideEffectOrder()
    {
        var o = new int[BlockSize];
        var p = new int[BlockSize];
        MixedHoistDeclarationKernels.LaunchSideEffectOrderKernel(o, p, 1, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(t * 10, o[t]);
            Assert.Equal(t * 10 + 1, p[t]);
        }
    }

    internal static void AssertAlternatingHoist()
    {
        var o = new int[BlockSize];
        var p = new int[BlockSize];
        MixedHoistDeclarationKernels.LaunchAlternatingHoistKernel(o, p, 1, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            // a = t, b = t + 1, c = 2t + 2, d = 3t + 2
            Assert.Equal((t + 1) * 1000 + 3 * t + 2, p[t]);
            Assert.Equal(t * 1000 + 2 * t + 2, o[t]);
        }
    }
}
