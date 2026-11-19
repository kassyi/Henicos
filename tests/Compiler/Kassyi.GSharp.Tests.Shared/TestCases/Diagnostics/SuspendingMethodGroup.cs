using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace SuspendingMethodGroupTest
{
    public static class Holder
    {
        [GpuDeviceFunction]
        public static void Step(int value)
        {
            Gpu.SyncThreads();
        }
    }

    public static class DeviceExtensions
    {
        [GpuDeviceFunction]
        public static void AfterBarrier(this int[] data)
        {
            Gpu.SyncThreads();
        }
    }

    public static partial class SuspendingMethodGroupKernels
    {
        [GpuDeviceFunction]
        public static void Step(int value)
        {
            Gpu.SyncThreads();
        }

        [GpuDeviceFunction]
        public static void StepUnit()
        {
            Gpu.SyncThreads();
        }

        [GpuDeviceFunction]
        public static int StepValue(int value)
        {
            Gpu.SyncThreads();
            return value + 1;
        }

        [GpuDeviceFunction]
        public static void NoBarrier(int value)
        {
        }

        [GpuDeviceFunction]
        public static void Apply(Action<int> action) => action(0);

        [GpuKernel]
        public static void DelegateVariableKernel(int[] data)
        {
            Action<int> a = Step;
            a(0);
        }

        [GpuKernel]
        public static void DelegateArgumentKernel(int[] data)
        {
            Apply(Step);
        }

        [GpuKernel]
        public static void StackLinqSelectKernel(int[] data)
        {
            data[0] = data.AsSpan(0, 1).AsStackEnumerable().Select(StepValue).Sum();
        }

        [GpuKernel]
        public static void QualifiedMethodGroupKernel(int[] data)
        {
            Action<int> a = Holder.Step;
            a(0);
        }

        [GpuKernel]
        public static void ReducedExtensionMethodGroupKernel(int[] data)
        {
            Action a = data.AfterBarrier;
            a();
        }

        [GpuKernel(Cooperative = true)]
        public static void MacroMethodGroupKernel(int[] data)
        {
            Gpu.ForUnroll(0, 2, Step);
            Gpu.SyncGrid(StepUnit);
        }

        [GpuKernel(Cooperative = true)]
        public static void ValidCallsKernel(int[] data)
        {
            Step(0);
            Action<int> a = NoBarrier;
            a(0);
            int len = nameof(Step).Length;
            Gpu.ForUnroll(0, 2, i => Step(i));
            Gpu.SyncGrid(() => Step(0));
            data[0] = len;
        }
    }
}
