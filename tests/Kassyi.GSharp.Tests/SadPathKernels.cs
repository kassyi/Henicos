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


    }
}
