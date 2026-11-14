using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="SwitchSectionSharedNamesKernels"/>. The native and managed test
/// classes share these scenarios and differ only in how a launch runs. Each expected value comes from the same
/// statements run as plain C#.
/// </summary>
internal static class SwitchSectionSharedNamesScenarios
{
    public static void OutVarAcrossSections(Action<Action> run)
    {
        int[] input = [0, 1, 2];
        int[] output = [0, 0, 0];

        run(() => SwitchSectionSharedNamesKernels.LaunchOutVarAcrossSectionsKernel(input, output, blockDimX: 3));

        // case 0: v = 10; case 1: v = 7; no case: untouched
        Assert.Equal([10, 7, 0], output);
    }

    public static void DeconstructionAcrossSections(Action<Action> run, int mode, int expected)
    {
        int[] output = [0];

        run(() => SwitchSectionSharedNamesKernels.LaunchDeconstructionAcrossSectionsKernel(output, mode, blockDimX: 1));

        Assert.Equal([expected], output);
    }

    public static void LocalFunctionAcrossSections(Action<Action> run, int mode, int expected)
    {
        int[] output = [0];

        run(() => SwitchSectionSharedNamesKernels.LaunchLocalFunctionAcrossSectionsKernel(output, mode, blockDimX: 1));

        Assert.Equal([expected], output);
    }

    public static void CapturingLocalFunctionAcrossSections(Action<Action> run, int mode, int expected)
    {
        int[] output = [0];

        run(() => SwitchSectionSharedNamesKernels.LaunchCapturingLocalFunctionAcrossSectionsKernel(output, mode,
            blockDimX: 1));

        Assert.Equal([expected], output);
    }

    public static void ChainedLocalFunctionAcrossSections(Action<Action> run, int mode, int expected)
    {
        int[] output = [0];

        run(() => SwitchSectionSharedNamesKernels.LaunchChainedLocalFunctionAcrossSectionsKernel(output, mode,
            blockDimX: 1));

        Assert.Equal([expected], output);
    }

    public static void SpanAcrossSections(Action<Action> run, int mode)
    {
        int[] a = [0, 0];
        int[] b = [0, 0];

        run(() => SwitchSectionSharedNamesKernels.LaunchSpanAcrossSectionsKernel(a, b, mode, blockDimX: 1));

        Assert.Equal(mode == 0 ? [1, 0] : [0, 0], a);
        Assert.Equal(mode == 0 ? [0, 0] : [2, 0], b);
    }

    public static void GotoCaseSharedOutVar(Action<Action> run)
    {
        int[] output = [0, 0, 0];

        run(() => SwitchSectionSharedNamesKernels.LaunchGotoCaseSharedOutVarKernel(output, blockDimX: 1));

        Assert.Equal([0, 10, 7], output);
    }

    public static void SharedInLoop(Action<Action> run)
    {
        int[] output = [0, 0];

        run(() => SwitchSectionSharedNamesKernels.LaunchSharedInLoopKernel(output, blockDimX: 1));

        // pass 0: v = 6 (case 0); pass 1: v = 5 (default)
        Assert.Equal([6, 5], output);
    }

    public static void NestedSwitchShared(Action<Action> run, int outer, int inner, int expected)
    {
        int[] output = [0];

        run(() => SwitchSectionSharedNamesKernels.LaunchNestedSwitchSharedKernel(output, outer, inner, blockDimX: 1));

        Assert.Equal([expected], output);
    }

    public static void SharedFunction(Action<Action> run, Action<int[], int> launch, int mode, int expected)
    {
        int[] output = [0];

        run(() => launch(output, mode));

        Assert.Equal([expected], output);
    }
}
