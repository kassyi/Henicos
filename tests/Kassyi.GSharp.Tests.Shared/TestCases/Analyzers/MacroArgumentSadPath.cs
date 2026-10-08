using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class GEN_36_MacroArgumentSadPath
{
    [GpuKernel]
    public static void MyKernel()
    {
        int count = 10;
        int sum = 0;
        
        // Error GSH0012: arg must be constant
        Gpu.ForUnroll(0, count, i => 
        {
            sum += i;
        });
        
        string ptxStr = "mov.u32 %0, %1;";
        // Error GSH0006: arg must be string literal
        Gpu.InlinePtx(ptxStr);

        // Error GSH0003: arg must be lambda expression
        // Intentionally passing null! to assert analyzer diagnostic when macro argument is not a lambda.
        Gpu.SyncGrid(null!);
    }
}
