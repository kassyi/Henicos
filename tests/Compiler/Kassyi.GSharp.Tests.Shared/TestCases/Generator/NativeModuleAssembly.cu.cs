using System;
using Kassyi.GSharp.Core;

namespace NativeModuleTest
{
    public static partial class Kernels
    {
        // A transpiler diagnostic inside a device function (string is unsupported)
        [GpuDeviceFunction]
        public static int WithString(int x)
        {
            string s = "x";
            return x;
        }

        // A device function that needs an include of its own
        [GpuDeviceFunction]
        public static void SortValues(Span<int> values)
        {
            values.Sort();
        }

        // Declared before its callee: on the CPU dialects its frame embeds Inner's, so Inner's body must come first
        [GpuDeviceFunction]
        public static int Outer(int x)
        {
            Gpu.SyncThreads();
            return Inner(x) + 1;
        }

        [GpuDeviceFunction]
        public static int Inner(int x)
        {
            Gpu.SyncThreads();
            return x * 2;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Run(int[] data)
        {
            int tid = ThreadIndex.X;
            data[tid] = WithString(data[tid]) + Outer(data[tid]);
            SortValues(data);
        }
    }
}
