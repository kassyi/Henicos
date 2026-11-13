namespace Kassyi.GSharp.Tests.Shared.Syntax;

// The launch of the kernel of a private struct, apart from the kernel file, which the snapshot tests compile alone
public static partial class PrivateStructParameterKernels
{
    /// <summary>
    /// Scales each element of <paramref name="values"/> by <paramref name="scale"/> and adds its index.
    /// </summary>
    public static void LaunchScale(int[] values, int scale) =>
        LaunchScaleKernel(new Bag { Values = values, Scale = scale }, blockDimX: (uint)values.Length);
}
