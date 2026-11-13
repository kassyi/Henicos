using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The scenarios of <see cref="LocalFunctionOrderKernels"/>; the native and managed classes share them.</summary>
internal static class LocalFunctionOrderScenarios
{
    public static void Run(Action<Action> run, Action<int[]> launch, Func<int, int> expected)
    {
        int[] data = [1, 2, 3, 4];

        run(() => launch(data));

        Assert.Equal([expected(1), expected(2), expected(3), expected(4)], data);
    }

    public static void NestedCallBeforeDeclaration(Action<Action> run, int flag)
    {
        int[] data = [1, 2, 3, 4];

        run(() => LocalFunctionOrderKernels.LaunchNestedCallBeforeDeclarationKernel(data, flag, blockDimX: 4));

        Assert.Equal(flag != 0 ? [101, 102, 103, 104] : [1, 2, 3, 4], data);
    }

    public static void SelfRecursion(Action<Action> run, int bonus)
    {
        int[] data = [0, 1, 2, 3];

        run(() => LocalFunctionRecursionKernels.LaunchSelfRecursionKernel(data, bonus, blockDimX: 4));

        // Sum(n) = 2n + Sum(n - 1), Sum(0) = bonus
        Assert.Equal([bonus, 2 + bonus, 6 + bonus, 12 + bonus], data);
    }

    public static void MutualRecursion(Action<Action> run)
    {
        int[] data = [0, 1, 2, 5];

        run(() => LocalFunctionRecursionKernels.LaunchMutualRecursionKernel(data, blockDimX: 4));

        // result * 100 + the n + 1 calls
        Assert.Equal([101, 2, 103, 6], data);
    }

    public static void VoidRecursion(Action<Action> run)
    {
        int[] data = [0, 1, 2, 4];

        run(() => LocalFunctionRecursionKernels.LaunchVoidRecursionKernel(data, blockDimX: 4));

        // n + 0 + 1 + ... + (n - 1)
        Assert.Equal([0, 1, 3, 10], data);
    }

    public static void RecursionBeforeDeclaration(Action<Action> run)
    {
        int[] data = [0, 1, 2, 4];

        run(() => LocalFunctionRecursionKernels.LaunchRecursionBeforeDeclarationKernel(data, blockDimX: 4));

        Assert.Equal([0, 3, 6, 12], data);
    }

    public static void SwitchSectionCallBeforeDeclaration(Action<Action> run, int mode)
    {
        int[] data = [1, 2, 3, 4];

        run(() => LocalFunctionOrderKernels.LaunchSwitchSectionCallBeforeDeclarationKernel(data, mode, blockDimX: 4));

        Assert.Equal(mode == 0 ? [1001, 1002, 1003, 1004] : [-1, -1, -1, -1], data);
    }

    public static void UsingBodyCallBeforeDeclaration(Action<Action> run)
    {
        int[] log = new int[4];
        int[] source = [1];
        int[] outs = [7];

        run(() => LocalFunctionOrderKernels.LaunchUsingBodyCallBeforeDeclarationKernel(log, source, outs, blockDimX: 1));

        // The local function ran, and the resource was disposed once at the end
        Assert.Equal([507], outs);
        Assert.Equal([2, 1, -1, 0], log);
    }

    public static void DelegateOfRecursive(Action<Action> run)
    {
        int[] data = [1, 2, 3, 4];

        run(() => LocalFunctionRecursionKernels.LaunchDelegateOfRecursiveKernel(data, blockDimX: 4));

        Assert.Equal([1, 2, 6, 24], data);
    }
}
