namespace Kassyi.Math.GpuBigInt;

#pragma warning disable IDE0051 // Remove unused private members
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

public class GpuBigInteger : IDisposable
{
    public IntPtr DevicePointer
    {
        get; private set;
    }
    public int NumWords
    {
        get;
    }
    private bool _disposed;

    public GpuBigInteger(int numWords)
    {
        NumWords = numWords;
        DevicePointer = NativeMethods.GpuBigInt_Alloc(numWords);
        if (DevicePointer == IntPtr.Zero)
        {
            throw new OutOfMemoryException($"Failed to allocate GPU memory for {numWords} words ({numWords * 4} bytes).");
        }
    }

    public GpuBigInteger(uint[] hostData) : this(hostData.Length)
    {
        if (!NativeMethods.GpuBigInt_CopyHostToDevice(DevicePointer, hostData, NumWords))
        {
            throw new InvalidOperationException($"Failed to copy host data ({hostData.Length} words) to GPU device pointer 0x{DevicePointer:X} (expected {NumWords} words).");
        }
    }

    public uint[] ToHostArray()
    {
        var hostData = new uint[NumWords];
        if (!NativeMethods.GpuBigInt_CopyDeviceToHost(hostData, DevicePointer, NumWords))
        {
            throw new InvalidOperationException($"Failed to copy {NumWords} words from GPU device pointer 0x{DevicePointer:X} to host.");
        }
        return hostData;
    }

    public void CopyFrom(GpuBigInteger source)
    {
        if (source.NumWords != NumWords)
        {
            throw new ArgumentException($"Size mismatch between GPU buffers: source has {source.NumWords} words, but target has {NumWords} words.", nameof(source));
        }
        if (!NativeMethods.GpuBigInt_CopyDeviceToDevice(DevicePointer, source.DevicePointer, NumWords))
        {
            throw new InvalidOperationException($"Failed to copy {NumWords} words on GPU from 0x{source.DevicePointer:X} to 0x{DevicePointer:X}.");
        }
    }

    public static void BitwiseAnd(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        if (a.NumWords != b.NumWords || a.NumWords != result.NumWords)
        {
            throw new ArgumentException($"All buffers must have the same size. a: {a.NumWords}, b: {b.NumWords}, result: {result.NumWords} words.");
        }

        NativeMethods.GpuBigInt_BitwiseAnd(a.DevicePointer, b.DevicePointer, result.DevicePointer, a.NumWords);
    }

    public static void Add(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        if (a.NumWords != b.NumWords || a.NumWords != result.NumWords)
        {
            throw new ArgumentException($"All buffers must have the same size. a: {a.NumWords}, b: {b.NumWords}, result: {result.NumWords} words.");
        }

        NativeMethods.GpuBigInt_Add(a.DevicePointer, b.DevicePointer, result.DevicePointer, a.NumWords);
    }

    public static void Sub(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        if (a.NumWords != b.NumWords || a.NumWords != result.NumWords)
        {
            throw new ArgumentException($"All buffers must have the same size. a: {a.NumWords}, b: {b.NumWords}, result: {result.NumWords} words.");
        }

        NativeMethods.GpuBigInt_Sub(a.DevicePointer, b.DevicePointer, result.DevicePointer, a.NumWords);
    }

    public static void ShiftLeft(GpuBigInteger a, int shiftBits, GpuBigInteger result)
    {
        if (a.NumWords != result.NumWords)
        {
            throw new ArgumentException($"Result buffer must have the same size as input buffer. a: {a.NumWords}, result: {result.NumWords} words.");
        }

        NativeMethods.GpuBigInt_ShiftLeft(a.DevicePointer, shiftBits, result.DevicePointer, a.NumWords);
    }

    public static void ShiftRight(GpuBigInteger a, int shiftBits, GpuBigInteger result)
    {
        if (a.NumWords != result.NumWords)
        {
            throw new ArgumentException($"Result buffer must have the same size as input buffer. a: {a.NumWords}, result: {result.NumWords} words.");
        }

        NativeMethods.GpuBigInt_ShiftRight(a.DevicePointer, shiftBits, result.DevicePointer, a.NumWords);
    }

    public bool Square(int logicalWords) => NativeMethods.GpuBigInt_Square(DevicePointer, logicalWords);

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
