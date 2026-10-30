#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The managed fallback calls the overload of an operator macro's target that the resolver chose (#478), whatever C#
/// would pick for the arguments as written (#711). The values are those of <see cref="OperatorMacroOverloadTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class OperatorMacroOverloadManagedTests : TranspilerTestBase
{
    private static readonly uint[] s_input = [1u, 2u, 0x10000000u, 0xFFFFFFFFu];

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_OverloadThatFits_IsCalledInEitherOrder()
    {
        var output = new uint[s_input.Length];
        var reversed = new uint[s_input.Length];

        ManagedKernelRun.Run(() =>
        {
            OperatorMacroOverloadKernels.LaunchShiftKernel(s_input, output, 5, blockDimX: (uint)s_input.Length);
            OperatorMacroOverloadKernels.LaunchReversedShiftKernel(s_input, reversed, 5,
                blockDimX: (uint)s_input.Length);
        });

        uint[] expected = [13u, 21u, 0x80000005u, 0xFFFFFFFDu];
        Assert.Equal(expected, output);
        Assert.Equal(expected, reversed);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_OverloadWithIdentityConversions_IsBetterThanUserDefinedConversions()
    {
        uint[] left = [1u, 2u, 3u, 4u];
        uint[] right = [10u, 20u, 30u, 40u];
        var output = new uint[left.Length];
        var cast = new uint[left.Length];

        ManagedKernelRun.Run(() =>
        {
            OperatorMacroOverloadKernels.LaunchPickKernel(left, right, output, blockDimX: (uint)left.Length);
            OperatorMacroOverloadKernels.LaunchCastPickKernel(left, right, cast, blockDimX: (uint)left.Length);
        });

        Assert.Equal([1011u, 1022u, 1033u, 1044u], output);
        Assert.Equal(output, cast);
    }

    // Shr(uint[], uint[], long) for the int shift: not the float overload that comes first, nor the byte overload that
    // the constant 3 alone would take
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_BetterConversionTarget_IsCalled()
    {
        var output = new uint[s_input.Length];
        var constant = new uint[s_input.Length];

        ManagedKernelRun.Run(() =>
        {
            OperatorMacroOverloadKernels.LaunchShrKernel(s_input, output, blockDimX: (uint)s_input.Length);
            OperatorMacroOverloadKernels.LaunchConstantShrKernel(s_input, constant, blockDimX: (uint)s_input.Length);
        });

        Assert.Equal([0u, 0u, 0x02000000u, 0x1FFFFFFFu], output);
        Assert.Equal(output, constant);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ValueParameter_IsBetterThanIn()
    {
        uint[] left = [10u, 20u, 30u, 40u];
        uint[] right = [1u, 2u, 3u, 4u];
        var output = new uint[left.Length];

        ManagedKernelRun.Run(() =>
            OperatorMacroOverloadKernels.LaunchSubKernel(left, right, output, blockDimX: (uint)left.Length));

        Assert.Equal([9u, 18u, 27u, 36u], output);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_OverloadWithContext_IsCalledWhenItIsBetter()
    {
        uint[] left = [1u, 2u, 3u, 4u];
        uint[] right = [10u, 20u, 30u, 40u];
        var output = new uint[left.Length];

        ManagedKernelRun.Run(() =>
            OperatorMacroOverloadKernels.LaunchContextOverloadKernel(left, right, output, 5,
                blockDimX: (uint)left.Length));

        Assert.Equal([16u, 27u, 38u, 49u], output);
    }
}
#endif
