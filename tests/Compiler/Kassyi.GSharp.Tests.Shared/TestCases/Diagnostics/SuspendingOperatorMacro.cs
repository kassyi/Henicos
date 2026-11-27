using Kassyi.GSharp.Core;

namespace SuspendingOperatorMacroTest
{
    public struct WarpSum
    {
        public int V;

        [GpuOperatorMacro(typeof(MacroKernels), nameof(MacroKernels.Add))]
        public static WarpSum operator +(WarpSum left, WarpSum right) => throw new GpuOnlyException();
    }

    public struct BarrierSum
    {
        public int V;

        [GpuOperatorMacro(typeof(MacroKernels), nameof(MacroKernels.AddB))]
        public static BarrierSum operator +(BarrierSum left, BarrierSum right) => throw new GpuOnlyException();
    }

    public struct PlainSum
    {
        public int V;

        [GpuOperatorMacro(typeof(MacroKernels), nameof(MacroKernels.AddPlain))]
        public static PlainSum operator +(PlainSum left, PlainSum right) => throw new GpuOnlyException();
    }

    public struct NestedWarpSum
    {
        public int V;

        [GpuOperatorMacro(typeof(MacroKernels), nameof(MacroKernels.AddToPlain))]
        public static PlainSum operator +(NestedWarpSum left, NestedWarpSum right) => throw new GpuOnlyException();
    }

    public struct ActiveTruth
    {
        public int V;

        [GpuOperatorMacro(typeof(MacroKernels), nameof(MacroKernels.AndActive))]
        public static ActiveTruth operator &(ActiveTruth left, ActiveTruth right) => throw new GpuOnlyException();

        public static bool operator true(ActiveTruth value) => value.V > 0;

        public static bool operator false(ActiveTruth value) => value.V <= 0;
    }

    public struct AccessorHolder
    {
        public int V;

        public int Summed
        {
            get
            {
                WarpSum x = default;
                x.V = V;
                WarpSum z = default;
                z = x + x;
                return z.V;
            }
        }
    }

    public static partial class MacroKernels
    {
        [GpuDeviceFunction]
        public static void Add(ref WarpSum result, WarpSum left, WarpSum right)
        {
            result.V = left.V + Warp.ShuffleXorSync(0xFFFFFFFFu, right.V, 1);
        }

        [GpuDeviceFunction]
        public static void AddB(ref BarrierSum result, BarrierSum left, BarrierSum right)
        {
            Gpu.SyncThreads();
            result.V = left.V + right.V;
        }

        [GpuDeviceFunction]
        public static void AddPlain(ref PlainSum result, PlainSum left, PlainSum right)
        {
            result.V = left.V + right.V;
        }

        [GpuDeviceFunction]
        public static void AddToPlain(ref PlainSum result, NestedWarpSum left, NestedWarpSum right)
        {
            result.V = left.V + Warp.ShuffleXorSync(0xFFFFFFFFu, right.V, 1);
        }

        [GpuDeviceFunction]
        public static void AndActive(ref ActiveTruth result, ActiveTruth left, ActiveTruth right)
        {
            uint mask = Warp.ActiveMask();
            result.V = left.V + Warp.ShuffleXorSync(mask, right.V, 1);
        }

        // Case A: warp-collective operator macro after a barrier in the kernel
        [GpuKernel]
        public static void MacroAfterBarrierKernel(int[] data)
        {
            int t = ThreadIndex.X;
            WarpSum x = default;
            x.V = data[t];
            Gpu.SyncThreads();
            WarpSum z = default;
            z = x + x;
            data[t] = z.V;
        }

        // Case B: barrier operator macro in a kernel without a barrier
        [GpuKernel]
        public static void BarrierMacroKernel(int[] data)
        {
            int t = ThreadIndex.X;
            BarrierSum x = default;
            x.V = data[t];
            BarrierSum z = default;
            z = x + x;
            data[t] = z.V;
        }

        // Case C: warp-collective operator macro in a kernel without a barrier
        [GpuKernel]
        public static void MacroWithoutBarrierKernel(int[] data)
        {
            int t = ThreadIndex.X;
            WarpSum x = default;
            x.V = data[t];
            WarpSum z = default;
            z = x + x;
            data[t] = z.V;
        }

        // Case D: nested operator macros where only the inner operator's target suspends
        [GpuKernel]
        public static void NestedInnerSuspendingKernel(int[] data)
        {
            int t = ThreadIndex.X;
            NestedWarpSum x = default;
            x.V = data[t];
            NestedWarpSum y = default;
            y.V = data[t];
            PlainSum w = default;
            w.V = 1;
            PlainSum z = default;
            z = (x + y) + w;
            data[t] = z.V;
        }

        // Case E: conditional && over a struct whose & operator macro target reaches Warp.ActiveMask()
        [GpuKernel]
        public static void ConditionalAndActiveMaskKernel(int[] data)
        {
            int t = ThreadIndex.X;
            ActiveTruth a = default;
            a.V = data[t];
            ActiveTruth b = default;
            b.V = data[t];
            ActiveTruth c = default;
            c = a && b;
            data[t] = c.V;
        }

        // Case F: operator macro whose target reaches neither a barrier nor a warp collective
        [GpuKernel]
        public static void NonSuspendingMacroKernel(int[] data)
        {
            int t = ThreadIndex.X;
            PlainSum x = default;
            x.V = data[t];
            PlainSum z = default;
            z = x + x;
            data[t] = z.V;
        }

        // Case G: suspending operator macro inside a property accessor of a struct that device code uses
        [GpuKernel]
        public static void StructPropertyAccessorKernel(int[] data)
        {
            int t = ThreadIndex.X;
            AccessorHolder h = default;
            h.V = data[t];
            data[t] = h.Summed;
        }

        // Case H: barrier-only suspending operator macro after a barrier in the kernel (native stack runs StackCppDialect)
        [GpuKernel]
        public static void BarrierMacroAfterBarrierKernel(int[] data)
        {
            int t = ThreadIndex.X;
            BarrierSum x = default;
            x.V = data[t];
            Gpu.SyncThreads();
            BarrierSum z = default;
            z = x + x;
            data[t] = z.V;
        }
    }
}
