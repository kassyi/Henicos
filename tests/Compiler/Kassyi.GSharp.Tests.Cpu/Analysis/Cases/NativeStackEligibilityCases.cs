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
        public static void ShuffleOnlyKernel(int[] data)
        {
            data[ThreadIndex.X] = Warp.ShuffleXorSync(0xFFFFFFFFu, ThreadIndex.X, 1);
        }

        // A kernel called like a function has no frame in the visit counts of its caller
        [GpuKernel]
        public static void ShuffleCallingShuffleKernel(int[] data)
        {
            ShuffleOnlyKernel(data);
            data[0] = Warp.ShuffleXorSync(0xFFFFFFFFu, data[0], 1);
        }

        // Only the state machine flattens a switch whose case values name no C++ label
        [GpuKernel]
        public static void FloatSwitchShuffleKernel(int[] data, float selector)
        {
            int value = ThreadIndex.X;
            switch (selector)
            {
                case 1f:
                    value = Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);
                    break;
                default:
                    value += 1;
                    break;
            }

            data[ThreadIndex.X] = value;
        }

        [GpuDeviceFunction]
        public static uint ActiveMaskHelper()
        {
            return Warp.ActiveMask();
        }

        [GpuKernel]
        public static void ActiveMaskDeviceFunctionKernel(int[] data)
        {
            data[ThreadIndex.X] = Warp.ShuffleXorSync(0xFFFFFFFFu, (int)ActiveMaskHelper(), 1);
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

namespace NativeStackOperatorMacroCases
{
    // An operator macro whose target reaches a warp collective: the expansion calls the target without the frame of the
    // stack model, which WarpFrameLayout gives only to the calls written as calls
    public struct WarpSum
    {
        public int V;

        [GpuOperatorMacro(typeof(WarpSumOps), nameof(WarpSumOps.Add))]
        public static WarpSum operator +(WarpSum left, WarpSum right) => throw new GpuOnlyException();

        [GpuOperatorMacro(typeof(WarpSumOps), "Subtract")]
        public static WarpSum operator -(WarpSum left, WarpSum right) => throw new GpuOnlyException();
    }

    public static partial class WarpSumOps
    {
        [GpuDeviceFunction]
        public static void Add(ref WarpSum result, WarpSum left, WarpSum right)
        {
            result.V = left.V + Warp.ShuffleXorSync(0xFFFFFFFFu, right.V, 1);
        }

        // A target the kernel cannot name in C# (the managed path calls it through an accessor stub)
        [GpuDeviceFunction]
        private static void Subtract(ref WarpSum result, WarpSum left, WarpSum right)
        {
            result.V = left.V - Warp.ShuffleXorSync(0xFFFFFFFFu, right.V, 1);
        }
    }

    public static partial class Kernels
    {
        [GpuKernel]
        public static void MacroAfterBarrierKernel(int[] data)
        {
            var x = new WarpSum { V = data[ThreadIndex.X] };
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            WarpSum z;
            z = x + x;
            data[ThreadIndex.X] = z.V;
        }

        [GpuKernel]
        public static void MacroAndShuffleKernel(int[] data)
        {
            var x = new WarpSum { V = Warp.ShuffleXorSync(0xFFFFFFFFu, data[ThreadIndex.X], 2) };
            WarpSum z;
            z = x + x;
            data[ThreadIndex.X] = z.V;
        }

        [GpuKernel]
        public static void PrivateMacroTargetKernel(int[] data)
        {
            var x = new WarpSum { V = data[ThreadIndex.X] };
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            WarpSum z;
            z = x - x;
            data[ThreadIndex.X] = z.V;
        }
    }
}
