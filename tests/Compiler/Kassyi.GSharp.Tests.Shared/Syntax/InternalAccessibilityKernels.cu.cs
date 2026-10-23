// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// The generated host launchers are a partial declaration of this class, so their accessibility must match.
internal static partial class InternalAccessibilityKernels
{
    [GpuKernel]
    public static void DoubleKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = input[tid] * 2;
    }
}
