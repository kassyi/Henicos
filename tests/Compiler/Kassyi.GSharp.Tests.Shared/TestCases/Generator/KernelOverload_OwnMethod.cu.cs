using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public static partial class OwnMethodKernels
    {
        // The warmup method of Foo is the kernel WarmupFunctionPointerFoo itself
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Foo(int[] data) { data[ThreadIndex.X] = 1; }

        [GpuKernel]
        public static void WarmupFunctionPointerFoo() { }
    }
}
