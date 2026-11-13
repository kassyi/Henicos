using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

file struct FileLocalScale
{
    public int Factor;
    public int Offset;
}

/// <summary>
/// A file-local type in a device function that the managed path calls as the user wrote it: no generated file names
/// the type, so the kernel runs on every dialect (GSH0115 reports only the bodies the managed path copies).
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
}
