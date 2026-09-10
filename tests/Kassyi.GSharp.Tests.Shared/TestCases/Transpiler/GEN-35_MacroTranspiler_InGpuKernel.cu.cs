using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Transpiler;

public class GEN_35_MacroTranspiler_InGpuKernel
{
    [GpuKernel]
    public static void MyKernel()
    {
        int sum = 0;
        
        // ForUnroll macro
        Gpu.ForUnroll(0, 3, i => 
        {
            sum += i;
        });
        
        // GridStrideLoop macro
        Gpu.GridStrideLoop(10, j => 
        {
            sum += j;
        });
        
        // SingleThread macro
        Gpu.SingleThread(() => 
        {
            sum += 100;
        });
    }
}
