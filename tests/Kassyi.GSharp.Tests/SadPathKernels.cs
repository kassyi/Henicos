using System;

namespace Kassyi.GSharp.Tests
{
    public class SadPathKernels
    {
        // 1. Unsupported OpCode (newobj, throw, callvirt, etc.)
        [GpuKernel]
        public static void ThrowExceptionKernel(uint a)
        {
            if (a == 0)
            {
                throw new ArgumentException("a cannot be 0");
            }
        }

        [GpuKernel]
        public static void UnsupportedMethodCall(uint a)
        {
            Console.WriteLine(a);
        }

        // 2. Unsupported Topology (goto, break, continue)
        [GpuKernel]
        public static void GotoSpaghettiKernel(uint a, uint[] result)
        {
            if (a == 1) goto skip;
            result[0] = a;
        skip:
            result[1] = a;
        }

        [GpuKernel]
        public static void EarlyReturnKernel(uint a, uint[] result)
        {
            if (a == 0) return;
            result[0] = a;
        }
    }
}
