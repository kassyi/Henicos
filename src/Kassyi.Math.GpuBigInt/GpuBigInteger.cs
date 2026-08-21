using System;

namespace Kassyi.Math.GpuBigInt
{
    public class GpuBigInteger : IDisposable
    {
        public IntPtr DevicePointer { get; private set; }
        public int NumWords { get; private set; }
        private bool _disposed = false;

        public GpuBigInteger(int numWords)
        {
            NumWords = numWords;
            DevicePointer = NativeMethods.GpuBigInt_Alloc(numWords);
            if (DevicePointer == IntPtr.Zero)
            {
                throw new OutOfMemoryException("Failed to allocate GPU memory.");
            }
        }

        public GpuBigInteger(uint[] hostData) : this(hostData.Length)
        {
            if (!NativeMethods.GpuBigInt_CopyHostToDevice(DevicePointer, hostData, NumWords))
            {
                throw new InvalidOperationException("Failed to copy data to GPU.");
            }
        }

        public uint[] ToHostArray()
        {
            uint[] hostData = new uint[NumWords];
            if (!NativeMethods.GpuBigInt_CopyDeviceToHost(hostData, DevicePointer, NumWords))
            {
                throw new InvalidOperationException("Failed to copy data from GPU.");
            }
            return hostData;
        }

        public void CopyFrom(GpuBigInteger source)
        {
            if (source.NumWords != NumWords)
            {
                throw new ArgumentException("Size mismatch between GPU buffers.");
            }
            if (!NativeMethods.GpuBigInt_CopyDeviceToDevice(DevicePointer, source.DevicePointer, NumWords))
            {
                throw new InvalidOperationException("Failed to copy data on GPU.");
            }
        }

        public static void BitwiseAnd(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
        {
            if (a.NumWords != b.NumWords || a.NumWords != result.NumWords)
                throw new ArgumentException("All arrays must have the same size.");
            NativeMethods.GpuBigInt_BitwiseAnd(a.DevicePointer, b.DevicePointer, result.DevicePointer, a.NumWords);
        }

        public static void Add(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
        {
            if (a.NumWords != b.NumWords || a.NumWords != result.NumWords)
                throw new ArgumentException("All arrays must have the same size.");
            NativeMethods.GpuBigInt_Add(a.DevicePointer, b.DevicePointer, result.DevicePointer, a.NumWords);
        }

        public static void Sub(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
        {
            if (a.NumWords != b.NumWords || a.NumWords != result.NumWords)
                throw new ArgumentException("All arrays must have the same size.");
            NativeMethods.GpuBigInt_Sub(a.DevicePointer, b.DevicePointer, result.DevicePointer, a.NumWords);
        }

        public static void ShiftLeft(GpuBigInteger a, int shiftBits, GpuBigInteger result)
        {
            if (a.NumWords != result.NumWords)
                throw new ArgumentException("Result array must have the same size as input.");
            NativeMethods.GpuBigInt_ShiftLeft(a.DevicePointer, shiftBits, result.DevicePointer, a.NumWords);
        }

        public static void ShiftRight(GpuBigInteger a, int shiftBits, GpuBigInteger result)
        {
            if (a.NumWords != result.NumWords)
                throw new ArgumentException("Result array must have the same size as input.");
            NativeMethods.GpuBigInt_ShiftRight(a.DevicePointer, shiftBits, result.DevicePointer, a.NumWords);
        }

        public bool Square(int logicalWords)
        {
            return NativeMethods.GpuBigInt_Square(DevicePointer, logicalWords);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (DevicePointer != IntPtr.Zero)
                {
                    NativeMethods.GpuBigInt_Free(DevicePointer);
                    DevicePointer = IntPtr.Zero;
                }
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        ~GpuBigInteger()
        {
            Dispose();
        }
    }
}
