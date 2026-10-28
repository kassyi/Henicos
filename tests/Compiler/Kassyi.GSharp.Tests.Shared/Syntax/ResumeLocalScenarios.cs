using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="ResumeLocalKernels"/>. The native and managed test classes share these
/// scenarios and differ only in how a launch runs.
/// </summary>
internal static class ResumeLocalScenarios
{
    public static void SpanBeforeBarrier(Action<Action> run)
    {
        var a = new int[2];

        run(() => ResumeLocalKernels.LaunchSpanBeforeBarrierKernel(a, blockDimX: 1));

        Assert.Equal(new[] { 1, 2 }, a);
    }

    public static void SpanInOtherBranch(Action<Action> run)
    {
        var a = new int[2];

        run(() => ResumeLocalKernels.LaunchSpanInOtherBranchKernel(a, 0, blockDimX: 1));

        Assert.Equal(new[] { 1, 0 }, a);
    }

    public static void ViewAfterLocal(Action<Action> run)
    {
        int[] first = [1, 2, 3, 4];
        int[] second = [10, 20, 30, 40];
        var result = new int[4];

        run(() => ResumeLocalKernels.LaunchViewAfterLocalKernel(first, second, result, blockDimX: 4));

        Assert.Equal(new[] { 101, 120, 103, 140 }, result);
    }

    public static void StackAllocInLoop(Action<Action> run)
    {
        int[] sums = [-1, -1, -1];

        run(() => ResumeLocalKernels.LaunchStackAllocInLoopKernel(sums, blockDimX: 1));

        Assert.Equal(new[] { 0, 0, 0 }, sums);
    }

    public static void ConstSize(Action<Action> run)
    {
        var a = new int[2];

        run(() => ResumeLocalKernels.LaunchConstSizeKernel(a, blockDimX: 1));

        Assert.Equal(new[] { 1, 2 }, a);
    }

    public static void MovingPointer(Action<Action> run)
    {
        var a = new int[2];

        run(() => ResumeLocalKernels.LaunchMovingPointerKernel(a, blockDimX: 1));

        Assert.Equal(new[] { 1, 2 }, a);
    }

    public static void MovingPointerBeforeBarrier(Action<Action> run)
    {
        var a = new int[3];

        run(() => ResumeLocalKernels.LaunchMovingPointerBeforeBarrierKernel(a, blockDimX: 1));

        Assert.Equal(new[] { 1, 2, 3 }, a);
    }

    public static void ReadOnlyElements(Action<Action> run)
    {
        var a = new int[2];

        run(() => ResumeLocalKernels.LaunchReadOnlyElementsKernel(a, blockDimX: 1));

        Assert.Equal(new[] { 30, 1 }, a);
    }

    public static void SiblingScopeSpan(Action<Action> run)
    {
        var a = new int[4];

        run(() => ResumeLocalKernels.LaunchSiblingScopeSpanKernel(a, blockDimX: 4));

        Assert.Equal(new[] { 3, 3, 3, 3 }, a);
    }

    public static void SameNameSpans(Action<Action> run)
    {
        var a = new int[4];

        run(() => ResumeLocalKernels.LaunchSameNameSpansKernel(a, blockDimX: 4));

        Assert.Equal(new[] { 4, 4, 4, 4 }, a);
    }

    public static void PointerBeforeBarrier(Action<Action> run)
    {
        var a = new int[2];

        run(() => ResumeLocalKernels.LaunchPointerBeforeBarrierKernel(a, blockDimX: 1));

        Assert.Equal(new[] { 3, 4 }, a);
    }

    public static void ConstSizeLength(Action<Action> run)
    {
        var a = new int[2];

        run(() => ResumeLocalKernels.LaunchConstSizeLengthKernel(a, blockDimX: 1));

        Assert.Equal(new[] { 4, 2 }, a);
    }

    public static void ZeroLengthPointer(Action<Action> run)
    {
        var a = new int[1];

        run(() => ResumeLocalKernels.LaunchZeroLengthPointerKernel(a, blockDimX: 1));

        Assert.Equal(new[] { 5 }, a);
    }

    public static void ZeroLengthPointerBeforeBarrier(Action<Action> run)
    {
        var a = new int[2];

        run(() => ResumeLocalKernels.LaunchZeroLengthPointerBeforeBarrierKernel(a, blockDimX: 1));

        Assert.Equal(new[] { 5, 6 }, a);
    }

    public static void PointerZeroed(Action<Action> run)
    {
        int[] a = [-1];

        run(() => ResumeLocalKernels.LaunchPointerZeroedKernel(a, blockDimX: 1));

        Assert.Equal(new[] { 0 }, a);
    }

    public static void ZeroLengthSpan(Action<Action> run)
    {
        var a = new int[1];

        run(() => ResumeLocalKernels.LaunchZeroLengthSpanKernel(a, blockDimX: 1));

        Assert.Equal(new[] { 5 }, a);
    }

    public static void ZeroLengthSpanBeforeBarrier(Action<Action> run)
    {
        var a = new int[2];

        run(() => ResumeLocalKernels.LaunchZeroLengthSpanBeforeBarrierKernel(a, blockDimX: 1));

        Assert.Equal(new[] { 5, 6 }, a);
    }

    public static void PointerElements(Action<Action> run)
    {
        var a = new int[1];

        run(() => ResumeLocalKernels.LaunchPointerElementsKernel(a, blockDimX: 1));

        Assert.Equal(new[] { 15 }, a);
    }

    public static void LambdaBeforeBarrier(Action<Action> run)
    {
        var result = new int[4];

        run(() => ResumeLocalKernels.LaunchLambdaBeforeBarrierKernel(result, blockDimX: 4));

        Assert.Equal(new[] { 11, 12, 13, 14 }, result);
    }
}
