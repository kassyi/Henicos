// ReSharper disable All
using Kassyi.GSharp.Core;
namespace TestSpace
{
    public class ManagedClass {}

    public static partial class ErrorKernels
    {
        [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
        public static int NonVoidReturnKernel()
        {
            return 1;
        }

        [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
        public static void ManagedParamKernel(ManagedClass obj)
        {
        }
    }
}
