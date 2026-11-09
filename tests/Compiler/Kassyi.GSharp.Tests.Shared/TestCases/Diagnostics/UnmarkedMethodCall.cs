#define UNMARKED_TRACE_ON
using System;
using System.Diagnostics;
using System.Numerics;
using Kassyi.GSharp.Core;

[assembly: BclIntrinsicMap("UnmarkedMethodCallTest.HostBits", "Lzc", ["System.UInt32"], "__gsharp_lzc_32")]

namespace UnmarkedMethodCallTest
{
    public struct Box
    {
        public int Value;

        public static Box Make(int v) { Box b; b.Value = v; return b; }

        public void Set(int v) { Value = v; }

        [GpuDeviceFunction]
        public static int Marked(int v) => v + 1;
    }

    public struct GBox<T> where T : struct
    {
        public T Value;

        public static GBox<T> Make(T v) { GBox<T> b; b.Value = v; return b; }
    }

    public static class Helpers
    {
        public static int Twice(int v) => v * 2;

        [GpuDeviceFunction]
        public static int Thrice(int v) => v * 3;

        [Conditional("UNMARKED_NEVER_DEFINED")]
        public static void TraceOff(int[] r) { r[0] = 1; }

        [Conditional("UNMARKED_TRACE_ON")]
        public static void TraceOn(int[] r) { r[0] = 2; }
    }

    public static class HostBits
    {
        public static int Lzc(uint x) => BitOperations.LeadingZeroCount(x);
    }

    public static partial class UnmarkedMethodCallKernels
    {
        [GpuKernel]
        public static void CallsKernel(int[] output)
        {
            var box = Box.Make(5);
            box.Set(7);
            output[0] = Helpers.Twice(box.Value);
            output[1] = GBox<int>.Make(3).Value;
            output[2] = Box.Marked(1) + Helpers.Thrice(2) + Math.Abs(output[3]) + Math.Max(output[4], 2);
            output[5] = HostBits.Lzc((uint)output[6]);
            Helpers.TraceOff(output);
            Helpers.TraceOn(output);
        }

        [GpuDeviceFunction]
        public static int Device(int v) => Helpers.Twice(v);
    }
}
