using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A kernel in a private class nested in a private class: only its containers can launch it
/// (<c>NestedKernelClassLaunchers.cs</c>), and the module context of the assembly, which cannot name it, warms it up
/// through them.
/// </summary>
public static partial class NestedKernelClassKernels
{
    private static partial class Middle
    {
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
