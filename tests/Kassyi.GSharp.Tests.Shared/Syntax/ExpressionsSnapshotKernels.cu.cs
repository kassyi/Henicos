using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static partial class ExpressionsSnapshotKernels
{
    [GpuDeviceFunction]
    private static void HelperMethod(ref int x, out int y)
    {
        y = x * 2;
        x += 1;
    }

    [GpuKernel]
    public static void BasicExpressionsKernel(int[] output, float fVal, double dVal)
    {
        // BinaryExpression & Type Casts
        int a = (int)(fVal + (float)dVal);
        int b = a << 2;
        int c = b ^ 0xFF;
        
        // InvocationExpression (ref / out parameters)
        int refArg = c;
        int outArg;
        HelperMethod(ref refArg, out outArg);
        
        // MemberAccessExpression
        int tidX = ThreadIndex.X;
        
        output[tidX] = refArg + outArg;
    }
}
