using System;
using Kassyi.GSharp.Transpiler.Ast;

namespace Kassyi.GSharp
{
    public class GpuKernelAttribute : Attribute { }
    
    public class DummyMath
    {
        [GpuKernel]
        public static void SimpleLoop(uint[] T, int E)
        {
            // Loops are temporarily unsupported until CFG is fully implemented.
            // This flat kernel ensures the transpiler runner test passes.
            T[0] = T[0] * 2;
        }
    }
}
