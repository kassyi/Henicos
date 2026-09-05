using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases;

public struct MyCustomStruct
{
    public int A;
    public float B;
}

public static class WrapperOptimizationKernels
{
    [GpuKernel]
    public static void ComplexSignatureKernel(
        ref int scalarRef,
        out float scalarOut,
        int[] arrayVal,
        ref MyCustomStruct structRef,
        [JitConstant] uint constantVal)
    {
        scalarOut = 0;
        if (scalarRef > constantVal)
        {
            scalarOut = arrayVal[0] + structRef.A;
            structRef.B = scalarOut;
        }
    }
}
