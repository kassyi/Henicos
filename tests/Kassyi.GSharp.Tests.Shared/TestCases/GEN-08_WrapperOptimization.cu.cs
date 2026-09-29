using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases;

public struct MyCustomStruct
{
    public int A;
    public float B;
}

public static partial class WrapperOptimizationKernels
{
    [GpuKernel]
    public static void ComplexSignatureKernel(
        ref int scalarRef,
        out float scalarOut,
        int[] arrayVal,
        ref MyCustomStruct structRef,
        [Constant] uint constantVal)
    {
        scalarOut = 0;
        if (scalarRef > constantVal)
        {
            scalarOut = arrayVal[0] + structRef.A;
            structRef.B = scalarOut;
        }
    }
}
