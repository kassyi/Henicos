namespace Kassyi.GSharp.Tests.Shared.Syntax;

// The launch of the private kernel class, apart from the kernel file, which the snapshot tests compile alone
public static partial class NestedKernelClassKernels
{
    /// <summary>Launches <c>Middle.Kernels.FillKernel</c> with one thread per element.</summary>
    public static void LaunchNestedFill(int[] output) => Middle.Launch(output);

    private static partial class Middle
    {
        public static void Launch(int[] output) => Kernels.LaunchFillKernel(output, blockDimX: (uint)output.Length);
    }
}
