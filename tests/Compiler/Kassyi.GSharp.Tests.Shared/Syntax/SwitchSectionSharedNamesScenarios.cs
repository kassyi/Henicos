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
}
