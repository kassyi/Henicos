#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Differential.ConditionalAccessTwinA;

// One partial kernel class in two files that import different extension classes with the same method names: a call
// through ?. binds the method its own file sees (ConditionalAccessTwinSecondKernels.cu.cs imports the other class)
namespace Kassyi.GSharp.Tests.Shared.Differential.ConditionalAccessTwinA
{
    internal static partial class TwinAExtensions
    {
        [GpuDeviceFunction]
        public static int TwinSum(this int[] values, long k) => values[0] + 1000;

        [GpuDeviceFunction]
        public static int TwinSame(this int[] values) => values[2] + 3000;
    }
}

namespace Kassyi.GSharp.Tests.Shared.Differential
{
    public static partial class ConditionalAccessTwinKernels
    {
        // C# binds TwinAExtensions.TwinSum(int[], long) and TwinAExtensions.TwinSame: this file imports only
        // ConditionalAccessTwinA. The barrier makes the managed path run the generated copy of the kernel
        [GpuKernel]
        public static void TwinFirstFileKernel(int[] a, int[] dst)
        {
            int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
            int[]? s = (a[t] & 1) == 0 ? null : a;
            Gpu.SyncThreads();

            int col = t * 2;
            dst[col + 0] = s?.TwinSum(1) ?? -1;
            dst[col + 1] = s?.TwinSame() ?? -1;
        }
    }
}
