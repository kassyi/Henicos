using Kassyi.GSharp.Core;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Tests.Shared.Generator;

public static partial class MultiMethodInvoker
{
    [CudaImport("shared_module", EntryPoint = "Kernel1")]
    public static partial void MethodOne([In] float[] input, float factor);

    [CudaImport("shared_module", EntryPoint = nameof(MethodTwo), PackContext = true)]
    public static partial void MethodTwo([Out] int[] output, int count);

    [CudaImport("other_module")]
    public static partial void MethodThree([In] float[] input, [Out] float[] output, int length);

    // A launch with dynamic shared memory opts the function in before the device allocations (#820)
    [CudaImport("other_module", EntryPoint = "SharedKernel")]
    public static partial void MethodFour([Out] uint[] output, int n, uint gridDimX, uint blockDimX, uint sharedMemBytes);
}

internal static partial class SecondaryInvoker
{
    [CudaImport("shared_module")]
    public static partial void SecondaryMethod([In] double[] data, double scale);
}
