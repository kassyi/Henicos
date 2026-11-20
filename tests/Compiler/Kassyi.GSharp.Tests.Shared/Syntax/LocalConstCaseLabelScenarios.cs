using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches <see cref="LocalConstCaseLabelKernels"/> and checks the values that C# defines, for the native CPU, the
/// managed CPU and the CUDA paths.
/// </summary>
internal static class LocalConstCaseLabelScenarios
{
    private const uint ThreadCount = 4;

    public static void CaseLabelInt(Action<Action> run)
    {
        var result = new int[ThreadCount];
        run(() => LocalConstCaseLabelKernels.LaunchCaseLabelKernel(result, blockDimX: ThreadCount));
        Assert.Equal([0, 0, 1, 0], result);
    }

    public static void CaseLabelByte(Action<Action> run)
    {
        var result = new int[ThreadCount];
        run(() => LocalConstCaseLabelKernels.LaunchByteCaseLabelKernel(result, blockDimX: ThreadCount));
        Assert.Equal([0, 0, 1, 0], result);
    }

    public static void GotoCase(Action<Action> run)
    {
        var result = new int[ThreadCount];
        run(() => LocalConstCaseLabelKernels.LaunchGotoCaseLocalConstKernel(result, blockDimX: ThreadCount));
        Assert.Equal([2, 2, 2, 2], result);
    }

    public static void SharedSection(Action<Action> run, int flag)
    {
        var result = new int[ThreadCount];
        run(() => LocalConstCaseLabelKernels.LaunchSharedSectionConstKernel(result, flag, blockDimX: ThreadCount));
        Assert.All(result, value => Assert.Equal(flag == 0 ? 10 : 11, value));
    }

    public static void InArgument(Action<Action> run)
    {
        var result = new int[ThreadCount];
        run(() => LocalConstCaseLabelKernels.LaunchInArgumentLocalConstKernel(result, blockDimX: ThreadCount));
        Assert.Equal([7, 8, 9, 10], result);
    }

    public static void LabeledConstAtBlockEnd(Action<Action> run)
    {
        var result = new int[ThreadCount];
        run(() => LocalConstCaseLabelKernels.LaunchLabeledConstAtBlockEndKernel(result, blockDimX: ThreadCount));
        Assert.Equal([0, 1, 1, 1], result);
    }

    public static void SharedMemoryCount(Action<Action> run)
    {
        var result = new int[ThreadCount];
        run(() => LocalConstCaseLabelKernels.LaunchLocalConstSharedMemoryCountKernel(result, blockDimX: ThreadCount));
        Assert.Equal([0, 1, 2, 3], result);
    }
}
