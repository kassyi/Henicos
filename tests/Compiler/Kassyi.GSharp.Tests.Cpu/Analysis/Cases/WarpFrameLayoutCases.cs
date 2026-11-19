using System.Diagnostics;
using Kassyi.GSharp.Core;

namespace WarpFrameLayoutCases
{
    public static partial class Kernels
    {
        // A sub-warp shuffle of the upper half before a full-warp shuffle: two sites
        [GpuDeviceFunction]
        public static int HalfThenFull(int v, int lane)
        {
            if (lane >= 16)
            {
                v += Warp.ShuffleXorSync(0xFFFF0000u, v, 1);
            }

            return v + Warp.ShuffleXorSync(0xFFFFFFFFu, v, 16);
        }

        // The same function from two call sites, no site of its own
        [GpuDeviceFunction]
        public static int HalfThenFullTwice(int v, int lane)
        {
            int w = HalfThenFull(v, lane);
            return HalfThenFull(w, lane) - w;
        }

        [GpuKernel]
        public static void NestedKernel(int[] input, int[] output)
        {
            int t = ThreadIndex.X;
            output[t] = HalfThenFullTwice(input[t], t & 31);
        }

        [GpuDeviceFunction]
        public static int FullPairSum(int v) => v + Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);

        [GpuKernel]
        public static void TwoCallSitesKernel(int[] data)
        {
            int t = ThreadIndex.X;
            int a = FullPairSum(data[t]);
            data[t] = FullPairSum(a);
        }

        // No site of its own; its first callee would share its offset without the frame's own slot
        [GpuDeviceFunction]
        public static int Relay(int v) => FullPairSum(v);

        [GpuDeviceFunction]
        public static int RelayTwice(int v) => Relay(Relay(v));

        // Sites before and after a call
        [GpuDeviceFunction]
        public static int Mixed(int v)
        {
            v = Warp.ShuffleDownSync(0xFFFFFFFFu, v, 1);
            v = Relay(v);
            Gpu.SyncWarp();
            return v + Warp.ReduceAddSync(0xFFFFFFFFu, v);
        }

        // A device function that reaches no collective: not a frame
        [GpuDeviceFunction]
        public static int Plain(int v) => v * 2;

        [GpuKernel]
        public static void EveryPathKernel(int[] data)
        {
            int t = ThreadIndex.X;
            int v = Relay(data[t]);
            v = RelayTwice(v) + Plain(v);
            v = Mixed(v);
            if (Warp.ActiveMask() != 0u)
            {
                v = FullPairSum(v);
            }

            data[t] = Relay(v);
        }

        // The body of Gpu.ForUnroll is written out twice by the CUDA writer, but it is one site and one call site
        [GpuKernel]
        public static void ForUnrollKernel(int[] data)
        {
            int t = ThreadIndex.X;
            int v = data[t];
            Gpu.ForUnroll(0, 2, k =>
            {
                v += Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1 << k);
                v = FullPairSum(v);
            });
            data[t] = v;
        }

        [GpuDeviceFunction]
        public static int RecursiveShuffle(int v, int depth)
        {
            if (depth <= 0)
            {
                return v;
            }

            return RecursiveShuffle(Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1), depth - 1);
        }

        [GpuKernel]
        public static void RecursiveKernel(int[] data)
        {
            int t = ThreadIndex.X;
            data[t] = RecursiveShuffle(data[t], 3);
        }

        [GpuDeviceFunction]
        public static int PingShuffle(int v, int depth) =>
            depth <= 0 ? Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1) : PongShuffle(v, depth - 1);

        [GpuDeviceFunction]
        public static int PongShuffle(int v, int depth) => PingShuffle(v + 1, depth);

        [GpuKernel]
        public static void MutuallyRecursiveKernel(int[] data)
        {
            int t = ThreadIndex.X;
            data[t] = PingShuffle(data[t], 3);
        }

        // Recursion that reaches no collective is no frame and does not keep the kernel from a layout
        [GpuDeviceFunction]
        public static int RecursivePlain(int v, int depth) => depth <= 0 ? v : RecursivePlain(v + 1, depth - 1);

        [GpuKernel]
        public static void RecursivePlainKernel(int[] data)
        {
            int t = ThreadIndex.X;
            data[t] = FullPairSum(RecursivePlain(data[t], 3));
        }

        // A kernel that reaches a shuffle, called like a function by another kernel: a kernel is no device function and
        // gets no frame
        [GpuKernel]
        public static void InnerShuffleKernel(int[] data)
        {
            int t = ThreadIndex.X;
            data[t] = Warp.ShuffleXorSync(0xFFFFFFFFu, data[t], 1);
        }

        [GpuKernel]
        public static void KernelCallingKernel(int[] data)
        {
            InnerShuffleKernel(data);
        }

        // C# removes the call (WARP_FRAME_LAYOUT_TRACE is not defined), so its shuffle is no frame
        [Conditional("WARP_FRAME_LAYOUT_TRACE")]
        [GpuDeviceFunction]
        public static void TraceShuffle(int[] data, int t)
        {
            data[t] = Warp.ShuffleXorSync(0xFFFFFFFFu, data[t], 1);
        }

        [GpuKernel]
        public static void RemovedCallKernel(int[] data)
        {
            int t = ThreadIndex.X;
            TraceShuffle(data, t);
            data[t] = FullPairSum(data[t]);
        }
    }
}
