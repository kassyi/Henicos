using System;
using System.Runtime.InteropServices;

namespace Kassyi.Math.GpuBigInt
{
    internal static class NativeMethods
    {
        private const string DllName = "Kassyi.Math.GpuBigInt.Cuda.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool GpuBigInt_Init(int maxWords);



        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool GpuBigInt_CopyDeviceToDevice(IntPtr d_dst, IntPtr d_src, int numWords);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool GpuBigInt_Square(IntPtr data, int numWords);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool GpuBigInt_Add(IntPtr d_a, IntPtr d_b, IntPtr d_result, int numWords);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool GpuBigInt_Sub(IntPtr d_a, IntPtr d_b, IntPtr d_result, int numWords);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool GpuBigInt_ShiftLeft(IntPtr d_a, int shiftBits, IntPtr d_result, int numWords);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool GpuBigInt_ShiftRight(IntPtr d_a, int shiftBits, IntPtr d_result, int numWords);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool GpuBigInt_BitwiseAnd(IntPtr d_a, IntPtr d_b, IntPtr d_result, int numWords);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GpuBigInt_Alloc(int numWords);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void GpuBigInt_Free(IntPtr ptr);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void GpuBigInt_Cleanup();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool GpuBigInt_CopyHostToDevice(IntPtr d_dst, uint[] h_src, int numWords);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool GpuBigInt_CopyDeviceToHost(uint[] h_dst, IntPtr d_src, int numWords);
    }
}
