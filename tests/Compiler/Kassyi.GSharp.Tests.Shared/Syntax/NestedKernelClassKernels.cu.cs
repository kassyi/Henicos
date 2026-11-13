using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A kernel in a private class nested in a private class: only its container can launch it, and the module context of
/// the assembly, which cannot name it, warms it up through the container.
/// </summary>
public static partial class NestedKernelClassKernels
{
    /// <summary>Launches <see cref="Middle.Kernels.FillKernel"/> with one thread per element.</summary>
    public static void LaunchNestedFill(int[] output) => Middle.Launch(output);

    private static partial class Middle
    {
        public static void Launch(int[] output) => Kernels.LaunchFillKernel(output, blockDimX: (uint)output.Length);

        private static partial class Kernels
        {
            [GpuKernel]
            public static void FillKernel(int[] output)
            {
                int i = ThreadIndex.X;
                output[i] = i * 3 + 1;
            }
        }
    }
}
