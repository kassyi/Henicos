using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

file struct FileLocalScale
{
    public int Factor;
    public int Offset;
}

/// <summary>
/// File-local types in bodies that the managed path does not copy (a device function called as written, a kernel that
/// runs as the user's method), so no generated file names the type and the kernel runs on every dialect and path; and in
/// a kernel that waits at a barrier, whose managed copy cannot name it (GSH0119): it runs on the native path and the
/// GPU, and its managed launch throws.
/// </summary>
public static partial class FileLocalTypeKernels
{
    [GpuDeviceFunction]
    private static int Apply(int value, int factor)
    {
        FileLocalScale scale = new FileLocalScale { Factor = factor, Offset = 1 };
        return value * scale.Factor + scale.Offset;
    }

    [GpuKernel]
    public static void ApplyKernel(int[] values, int factor)
    {
        int i = ThreadIndex.X;
        values[i] = Apply(values[i], factor);
    }

    [GpuKernel]
    public static void ScaleKernel(int[] values, int factor)
    {
        int i = ThreadIndex.X;
        FileLocalScale scale = new FileLocalScale { Factor = factor, Offset = 1 };
        values[i] = values[i] * scale.Factor + scale.Offset;
    }

    [GpuKernel]
    public static void BarrierScaleKernel(int[] values, int factor)
    {
        int i = ThreadIndex.X;
        FileLocalScale scale = new FileLocalScale { Factor = factor, Offset = 1 };
        Gpu.SyncThreads();
        values[i] = values[i] * scale.Factor + scale.Offset;
    }

    // A non-constant integer division is rewritten by the managed path (int.MinValue / -1), so that path needs a copy
    [GpuKernel]
    public static void DivideKernel(int[] values, int divisor)
    {
        int i = ThreadIndex.X;
        FileLocalScale scale = new FileLocalScale { Factor = divisor, Offset = 1 };
        values[i] = values[i] / scale.Factor * 10 + values[i] % scale.Factor;
    }
}
