using Kassyi.GSharp.Core;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Tests.Shared.Generator;

public static partial class TestInvoker
{
    [CudaImport("my_fatbin", EntryPoint = "MyKernel")]
    public static partial void LaunchMyKernel(
        [In] float[] a, 
        [Out] float[] b, 
        float scalarValue, 
        uint gridDimX, 
        uint blockDimX);
}
