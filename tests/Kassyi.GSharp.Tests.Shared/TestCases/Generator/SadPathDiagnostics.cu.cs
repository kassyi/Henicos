// ReSharper disable All
using Kassyi.GSharp.Core;
namespace TestSpace
{
    public static class ErrorSpecKernel
    {
        [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
        public static int BadKernel()
        {
            string s = "hello";
            object obj = new object();
            return 0;
        }
    }
}
