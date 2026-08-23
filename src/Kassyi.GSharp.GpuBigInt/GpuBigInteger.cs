
using Kassyi.GSharp.GpuBigInt.Expressions;

namespace Kassyi.GSharp.GpuBigInt;
using Kassyi.GSharp.GpuBigInt.Kernels;
using Kassyi.GSharp.GpuBigInt.Contexts;
using Kassyi.GSharp.GpuBigInt.Interop;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
public class GpuBigInteger : IDisposable, IGpuBigIntExpression
{
    public IntPtr DevicePointer
    {
        get; private set;
    }
    public int NumWords
    {
        get;
    }

    public GpuContext Context
    {
        get;
    }

    internal bool IsTemp
    {
        get; set;
    }

    private bool _disposed;

    public GpuBigInteger(GpuContext context, int numWords)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        NumWords = numWords;

        DevicePointer = Context.Provider.AllocateMemory(numWords);

        if (DevicePointer == IntPtr.Zero)
        {
            throw new OutOfMemoryException(
                $"Failed to allocate GPU memory for {numWords} words ({numWords * 4} bytes).");
        }
    }

    public GpuBigInteger(GpuContext context, uint[] hostData) : this(context, hostData.Length)
    {
        Context.Provider.CopyHostToDevice(DevicePointer, hostData, NumWords);
    }

    public GpuBigInteger Evaluate(GpuContext context, GpuBigInteger? targetResult = null)
    {
        if (targetResult == null || targetResult == this)
        {
            return this;
        }

        targetResult.CopyFrom(this);
        return targetResult;
    }

    public GpuBigIntExpr Value
    {
        get => new(this);
        set => value.Node.Evaluate(Context, this);
    }

    public static GpuBigIntExpr operator +(GpuBigInteger a, GpuBigInteger b) => new(new AddExpression(a, b));
    public static GpuBigIntExpr operator -(GpuBigInteger a, GpuBigInteger b) => new(new SubExpression(a, b));
    public static GpuBigIntExpr operator *(GpuBigInteger a, GpuBigInteger b) => new(new MulExpression(a, b));
    public static GpuBigIntExpr operator &(GpuBigInteger a, GpuBigInteger b) => new(new BitwiseAndExpression(a, b));

    public static GpuBigIntExpr operator <<(GpuBigInteger a, int shiftBits) =>
        new(new ShiftLeftExpression(a, shiftBits));

    public static GpuBigIntExpr operator >>(GpuBigInteger a, int shiftBits) =>
        new(new ShiftRightExpression(a, shiftBits));

    public uint[] ToHostArray()
    {
        var hostData = new uint[NumWords];
        Context.Provider.CopyDeviceToHost(hostData, DevicePointer, NumWords);

        return hostData;
    }

    public void CopyFrom(GpuBigInteger source)
    {
        if (source.NumWords != NumWords)
        {
            throw new ArgumentException(
                $"Size mismatch between GPU buffers: source has {source.NumWords} words, but target has {NumWords} words.",
                nameof(source));
        }

        Context.Provider.CopyDeviceToDevice(DevicePointer, source.DevicePointer, NumWords);
    }

    private static void ThrowIfSizeMismatch(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        if (a.NumWords != b.NumWords || a.NumWords != result.NumWords)
        {
            throw new ArgumentException(
                $"All buffers must have the same size. a: {a.NumWords}, b: {b.NumWords}, result: {result.NumWords} words.");
        }
    }

    private static void ThrowIfSizeMismatch(GpuBigInteger a, GpuBigInteger result)
    {
        if (a.NumWords != result.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must have the same size as input buffer. a: {a.NumWords}, result: {result.NumWords} words.");
        }
    }

    public static void BitwiseAnd(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        ThrowIfSizeMismatch(a, b, result);
        a.Context.Provider.BitwiseAnd(a.Context, a, b, result);
    }

    public static void Add(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        ThrowIfSizeMismatch(a, b, result);
        a.Context.Provider.Add(a.Context, a, b, result);
    }

    public static void Sub(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        ThrowIfSizeMismatch(a, b, result);
        a.Context.Provider.Sub(a.Context, a, b, result);
    }

    public static void Mul(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        if (result.NumWords != a.NumWords + b.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must be the sum of input buffer sizes for Mul. a: {a.NumWords}, b: {b.NumWords}, result: {result.NumWords} words.");
        }
        a.Context.Provider.Mul(a.Context, a, b, result);
    }

    public static void ShiftLeft(GpuBigInteger a, int shiftBits, GpuBigInteger result)
    {
        ThrowIfSizeMismatch(a, result);
        a.Context.Provider.ShiftLeft(a.Context, a, shiftBits, result);
    }

    public static void ShiftRight(GpuBigInteger a, int shiftBits, GpuBigInteger result)
    {
        ThrowIfSizeMismatch(a, result);
        a.Context.Provider.ShiftRight(a.Context, a, shiftBits, result);
    }

    public bool Square(int logicalWords) => Context.Provider.Square(Context, DevicePointer, logicalWords);

    internal void DisposeCore()
    {
        if (!_disposed)
        {
            if (DevicePointer != IntPtr.Zero)
            {
                Context.Provider.FreeMemory(DevicePointer);

                DevicePointer = IntPtr.Zero;
            }

            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (IsTemp)
        {
            Context.ReturnTemp(this);
            return;
        }

        DisposeCore();
    }

    ~GpuBigInteger()
    {
        Dispose();
    }
}

