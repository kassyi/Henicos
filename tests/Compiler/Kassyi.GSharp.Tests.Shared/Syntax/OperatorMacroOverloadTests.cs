using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// An operator macro calls the overload of its target that C# overload resolution would pick for the operator's types,
/// whatever the declaration order (#478), on the GPU and the native CPU build.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class OperatorMacroOverloadTests : TranspilerTestBase
{
    private static readonly uint[] s_input = [1u, 2u, 0x10000000u, 0xFFFFFFFFu];

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_OverloadThatFitsComesSecond_IsCalled()
    {
        var output = new uint[s_input.Length];

        OperatorMacroOverloadKernels.LaunchShiftKernel(s_input, output, 5, blockDimX: (uint)s_input.Length);

        Assert.Equal(ExpectedShift(5), output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_OverloadThatFitsComesFirst_IsCalled()
    {
        var output = new uint[s_input.Length];

        OperatorMacroOverloadKernels.LaunchReversedShiftKernel(s_input, output, 5, blockDimX: (uint)s_input.Length);

        Assert.Equal(ExpectedShift(5), output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_OverloadWithIdentityConversions_IsBetterThanUserDefinedConversions()
    {
        uint[] left = [1u, 2u, 3u, 4u];
        uint[] right = [10u, 20u, 30u, 40u];
        var output = new uint[left.Length];

        OperatorMacroOverloadKernels.LaunchPickKernel(left, right, output, blockDimX: (uint)left.Length);

        Assert.Equal([1011u, 1022u, 1033u, 1044u], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_OperatorInsideCastAndParentheses_CallsTheSameOverload()
    {
        uint[] left = [1u, 2u, 3u, 4u];
        uint[] right = [10u, 20u, 30u, 40u];
        var output = new uint[left.Length];

        OperatorMacroOverloadKernels.LaunchCastPickKernel(left, right, output, blockDimX: (uint)left.Length);

        Assert.Equal([1011u, 1022u, 1033u, 1044u], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_BetterConversionTarget_IsCalled()
    {
        var output = new uint[s_input.Length];

        OperatorMacroOverloadKernels.LaunchShrKernel(s_input, output, blockDimX: (uint)s_input.Length);

        Assert.Equal([0u, 0u, 0x02000000u, 0x1FFFFFFFu], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ValueParameter_IsBetterThanIn()
    {
        uint[] left = [10u, 20u, 30u, 40u];
        uint[] right = [1u, 2u, 3u, 4u];
        var output = new uint[left.Length];

        OperatorMacroOverloadKernels.LaunchSubKernel(left, right, output, blockDimX: (uint)left.Length);

        Assert.Equal([9u, 18u, 27u, 36u], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_OverloadWithContext_IsCalledWhenItIsBetter()
    {
        uint[] left = [1u, 2u, 3u, 4u];
        uint[] right = [10u, 20u, 30u, 40u];
        var output = new uint[left.Length];

        OperatorMacroOverloadKernels.LaunchContextOverloadKernel(left, right, output, 5, blockDimX: (uint)left.Length);

        Assert.Equal([16u, 27u, 38u, 49u], output);
    }

    // (value << 3) + bias, as ShiftOps.Shift(ref ShiftCtx, uint[], uint[], int) computes it
    private static uint[] ExpectedShift(int bias)
    {
        var expected = new uint[s_input.Length];
        for (var i = 0; i < expected.Length; i++)
        {
            expected[i] = unchecked((s_input[i] << 3) + (uint)bias);
        }

        return expected;
    }
}
