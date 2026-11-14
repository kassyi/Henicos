using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="RefLocalKernels"/>. The native and managed test classes share these
/// scenarios and differ only in how a launch runs. Each expected value comes from the same statements run as plain C#.
/// </summary>
internal static class RefLocalScenarios
{
    public static void AliasOfLocal(Action<Action> run)
    {
        int[] r = [10, 20];

        run(() => RefLocalKernels.LaunchAliasOfLocalKernel(r, blockDimX: 1));

        Assert.Equal([11, 20], r);
    }

    public static void AliasOfElement(Action<Action> run)
    {
        int[] r = [10, 20];

        run(() => RefLocalKernels.LaunchAliasOfElementKernel(r, blockDimX: 1));

        Assert.Equal([10, 41], r);
    }

    public static void Rebind(Action<Action> run)
    {
        int[] r = [0, 0];

        run(() => RefLocalKernels.LaunchRebindKernel(r, blockDimX: 1));

        // a = ref y; a = 5; changes y, and x keeps its value
        Assert.Equal([1, 5], r);
    }

    public static void Conditional(Action<Action> run, int c, int[] expected)
    {
        int[] r = [0, 0];

        run(() => RefLocalKernels.LaunchConditionalKernel(r, c, blockDimX: 1));

        Assert.Equal(expected, r);
    }

    public static void RefReadOnly(Action<Action> run)
    {
        int[] r = [1, 2];

        run(() => RefLocalKernels.LaunchRefReadOnlyKernel(r, blockDimX: 1));

        // The ref readonly local reads the element after the other write
        Assert.Equal([7, 7], r);
    }

    public static void ForEachRefSpan(Action<Action> run)
    {
        int[] r = [1, 2, 3, 4];

        run(() => RefLocalKernels.LaunchForEachRefSpanKernel(r, blockDimX: 4));

        Assert.Equal([2, 3, 4, 5], r);
    }

    public static void ForEachRefArray(Action<Action> run)
    {
        int[] r = [1, 2, 3, 4, 5, 6];

        run(() => RefLocalKernels.LaunchForEachRefArrayKernel(r, blockDimX: 1));

        // 3 is skipped, the loop leaves at 5
        Assert.Equal([2, 4, 3, 8, 5, 6], r);
    }

    public static void GotoPastRefLocal(Action<Action> run, int flag, int expectedFirst)
    {
        int[] r = [10, 20];

        run(() => RefLocalKernels.LaunchGotoPastRefLocalKernel(r, flag, blockDimX: 1));

        Assert.Equal([expectedFirst, 7], r);
    }
}
