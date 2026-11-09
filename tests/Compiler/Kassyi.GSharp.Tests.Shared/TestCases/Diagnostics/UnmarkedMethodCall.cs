using System;
using Kassyi.GSharp.Core;

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
        }

        [GpuDeviceFunction]
        public static int Device(int v) => Helpers.Twice(v);
    }
}
