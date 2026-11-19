using System;
using System.Diagnostics;
using Kassyi.GSharp.Core;

namespace NativeStackEligibilityCases
{
    public static class Gpu
    {
        public static void SyncThreads()
        {
        }
    }

    public static partial class Kernels
    {
        [GpuDeviceFunction]
        public static void SuspendingHelper(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }

        [GpuDeviceFunction]
        public static void OuterSuspendingHelper(int[] data)
        {
            SuspendingHelper(data);
        }

        [GpuDeviceFunction]
        public static int ShuffleHelper(int x)
        {
            return Warp.ShuffleXorSync(0xFFFFFFFFu, x, 1);
        }

        [GpuDeviceFunction]
        public static int OuterShuffleHelper(int x)
        {
            return ShuffleHelper(x);
        }

        [GpuDeviceFunction]
        public static void GridSyncHelper(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Kassyi.GSharp.Core.Gpu.SyncGrid();
            data[0] = 1;
        }

        [GpuDeviceFunction]
        public static void DirectlyRecursiveBarrier(int depth, int[] data)
        {
            if (depth <= 0)
            {
                return;
            }

            Kassyi.GSharp.Core.Gpu.SyncThreads();
            DirectlyRecursiveBarrier(depth - 1, data);
        }

        [GpuDeviceFunction]
        public static void MutualPing(int depth, int[] data)
        {
            if (depth <= 0)
            {
                return;
            }

            Kassyi.GSharp.Core.Gpu.SyncThreads();
            MutualPong(depth - 1, data);
        }

        [GpuDeviceFunction]
        public static void MutualPong(int depth, int[] data)
        {
            if (depth <= 0)
            {
                return;
            }

            MutualPing(depth - 1, data);
        }

        [GpuDeviceFunction]
        public static void WrapperCallingRecursiveBarrier(int depth, int[] data)
        {
            DirectlyRecursiveBarrier(depth, data);
        }

        [Conditional("GSHARP_NEVER_DEFINED_CONDITION")]
        private static void OmittedBarrierCall()
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
        }

        [GpuKernel]
        public static void DirectBarrierKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }

        [GpuKernel]
        public static void LocalFunctionBarrierKernel(int[] data)
        {
            void Step()
            {
                Kassyi.GSharp.Core.Gpu.SyncThreads();
            }

            Step();
            data[0] = 1;
        }

        [GpuKernel]
        public static void LambdaBarrierKernel(int[] data)
        {
            Action step = () => Kassyi.GSharp.Core.Gpu.SyncThreads();
            step();
            data[0] = 1;
        }

        [GpuKernel]
        public static void NoBarrierKernel(int[] data)
        {
            data[0] = 1;
        }

        [GpuKernel]
        public static void WarpShuffleAndBarrierKernel(int[] data)
        {
            int v = Warp.ShuffleXorSync(0xFFFFFFFFu, ThreadIndex.X, 1);
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[ThreadIndex.X] = v;
        }

        [GpuKernel]
        public static void WarpActiveMaskAndBarrierKernel(int[] data)
        {
            uint mask = Warp.ActiveMask();
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[ThreadIndex.X] = (int)mask;
        }

        [GpuKernel]
        public static void GridSyncAndBarrierKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            Kassyi.GSharp.Core.Gpu.SyncGrid();
            data[0] = 1;
        }

        [GpuKernel(Cooperative = true)]
        public static void CooperativeBarrierKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }

        [GpuKernel]
        public static void SuspendingDeviceFunctionKernel(int[] data)
        {
            SuspendingHelper(data);
        }

        [GpuKernel]
        public static void MultiLevelSuspendingKernel(int[] data)
        {
            OuterSuspendingHelper(data);
        }

        [GpuKernel]
        public static void ShuffleDeviceFunctionKernel(int[] data)
        {
            data[0] = ShuffleHelper(ThreadIndex.X);
        }

        [GpuKernel]
        public static void MultiLevelShuffleKernel(int[] data)
        {
            data[0] = OuterShuffleHelper(ThreadIndex.X);
        }

        [GpuKernel]
        public static void GridSyncDeviceFunctionKernel(int[] data)
        {
            GridSyncHelper(data);
        }

        [GpuKernel]
        public static void DirectRecursiveSuspendingKernel(int depth, int[] data)
        {
            DirectlyRecursiveBarrier(depth, data);
        }

        [GpuKernel]
        public static void MutuallyRecursiveSuspendingKernel(int depth, int[] data)
        {
            MutualPing(depth, data);
        }

        [GpuKernel]
        public static void WrapperCallingRecursiveSuspendingKernel(int depth, int[] data)
        {
            WrapperCallingRecursiveBarrier(depth, data);
        }

        [GpuKernel]
        public static void UserLookalikeBarrierKernel(int[] data)
        {
            Gpu.SyncThreads();
            data[0] = 1;
        }

        [GpuKernel]
        public static void ConditionalOmittedCallKernel(int[] data)
        {
            OmittedBarrierCall();
            data[0] = 1;
        }
    }

    public static partial class UsingStaticKernels
    {
        [GpuKernel]
        public static void UsingStaticBarrierKernel(int[] data)
        {
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            data[0] = 1;
        }
    }
}

namespace NativeStackUsingStaticCases
{
    using static Kassyi.GSharp.Core.Gpu;

    public static partial class Kernels
    {
        [GpuKernel]
        public static void UsingStaticSyncThreadsKernel(int[] data)
        {
            SyncThreads();
            data[0] = 1;
        }
    }
}
