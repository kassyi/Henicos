using System;
using Kassyi.GSharp.Transpiler.Ast;

namespace Kassyi.GSharp
{
    public class GpuKernelAttribute : Attribute { }
    
    public class DummyMath
    {
        [GpuKernel]
        public static void SimpleLoop(int T)
        {
            for (int i = 0; i < 100; i++)
            {
                T = T * 2;
                T = T + 1;
            }
        }
    }
}
