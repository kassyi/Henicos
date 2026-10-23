using Kassyi.GSharp.Core;

namespace TestNamespace.SplitOverload;

// A host method with the kernels' name; a test adds or removes the kernel attribute on it by replacing the text.
// The file names no kernel attribute, so the snapshot test case list leaves it out
public static partial class SplitKernels
{
    public static void ScaleKernel(double[] data, double factor)
    {
        int i = ThreadIndex.X;
        data[i] = data[i] * factor;
    }
}
