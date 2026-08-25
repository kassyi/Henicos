using Kassyi.GSharp.Core;
namespace TestSpace
{
    public class ManagedClass {}

    public static class ErrorKernels
    {
        [GpuKernel]
        public static int NonVoidReturnKernel()
        {
            return 1;
        }

        [GpuKernel]
        public static void ManagedParamKernel(ManagedClass obj)
        {
        }
    }
}
