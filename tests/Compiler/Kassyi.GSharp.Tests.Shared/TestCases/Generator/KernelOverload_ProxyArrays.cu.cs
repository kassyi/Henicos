using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public struct ItemsA { public int[] Items; }

    public struct ItemsB { public int[] Items; }

    public static partial class ProxyArrayKernels
    {
        // Arrays of structs with array fields have no GpuBuffer<T>: both typed launchers take a GSharpArrayProxy
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Run(ItemsA[] bags) { }

        [GpuKernel]
        public static void Run(ItemsB[] bags) { }
    }
}
