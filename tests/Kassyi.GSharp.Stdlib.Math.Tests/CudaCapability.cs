using System;
using Xunit;

namespace Kassyi.GSharp.Stdlib.Math.Tests
{
    public static class CudaCapability
    {
        public static void EnsureGpuAvailable()
        {
            bool available = false;
            try
            {
                Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuInit(0);
                available = true;
            }
            catch
            {
                available = false;
            }
            Skip.IfNot(available, "NVIDIA GPU and CUDA Driver are required to run this test.");
        }
    }
}
