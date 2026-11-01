// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The context that <see cref="ShiftOps"/> and <see cref="ReversedShiftOps"/> take first.</summary>
public struct ShiftCtx
{
    public int Bias;
}

// Issue #478: the operator macro names an overloaded method. The overload with the same parameter count that comes
// first in declaration order takes neither ShiftRef nor uint[]; the one that fits comes second.
public static partial class ShiftOps
{
    [GpuDeviceFunction]
    public static void Shift(ShiftCtx ctx, int wordShift, int bitShift)
    {
    }

    [GpuDeviceFunction]
    public static void Shift(ref ShiftCtx ctx, uint[] result, uint[] value, int shift)
    {
        int tid = ThreadIndex.X;
        result[tid] = (value[tid] << shift) + (uint)ctx.Bias;
    }
}

// The same overloads in the other order: the choice must not depend on it
public static partial class ReversedShiftOps
{
    [GpuDeviceFunction]
    public static void Shift(ref ShiftCtx ctx, uint[] result, uint[] value, int shift)
    {
        int tid = ThreadIndex.X;
        result[tid] = (value[tid] << shift) + (uint)ctx.Bias;
    }

    [GpuDeviceFunction]
    public static void Shift(ShiftCtx ctx, int wordShift, int bitShift)
    {
    }
}

public readonly ref struct ShiftRef
{
    public readonly uint[] Data;

    public ShiftRef(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(ShiftOps), nameof(ShiftOps.Shift))]
    public static ShiftRef operator <<(ShiftRef value, int shift) => throw new GpuOnlyException();

    public static implicit operator ShiftRef(uint[] data) => new(data);

    public static implicit operator uint[](ShiftRef value) => value.Data;
}

public readonly ref struct ReversedShiftRef
{
    public readonly uint[] Data;

    public ReversedShiftRef(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(ReversedShiftOps), nameof(ReversedShiftOps.Shift))]
    public static ReversedShiftRef operator <<(ReversedShiftRef value, int shift) => throw new GpuOnlyException();

    public static implicit operator ReversedShiftRef(uint[] data) => new(data);

    public static implicit operator uint[](ReversedShiftRef value) => value.Data;
}

// Both overloads apply; the one that takes PickRef itself (identity conversions) is better than the one that takes
// uint[] (user-defined conversions), as C# overload resolution decides. The two add different amounts.
public static partial class PickOps
{
    public const uint ArrayOverloadAdds = 0u;
    public const uint ExactOverloadAdds = 1000u;

    [GpuDeviceFunction]
    public static void Add(uint[] result, uint[] left, uint[] right)
    {
        int tid = ThreadIndex.X;
        result[tid] = left[tid] + right[tid] + ArrayOverloadAdds;
    }

    [GpuDeviceFunction]
    public static void Add(PickRef result, PickRef left, PickRef right)
    {
        int tid = ThreadIndex.X;
        result.Data[tid] = left.Data[tid] + right.Data[tid] + ExactOverloadAdds;
    }
}

public readonly ref struct PickRef
{
    public readonly uint[] Data;

    public PickRef(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(PickOps), nameof(PickOps.Add))]
    public static PickRef operator +(PickRef left, PickRef right) => throw new GpuOnlyException();

    public static implicit operator PickRef(uint[] data) => new(data);

    public static implicit operator uint[](PickRef value) => value.Data;
}

/// <summary>
/// Operator macros whose target method is overloaded (#478). <see cref="OperatorMacroOverloadTests"/> checks the values
/// on the GPU and the native CPU build, <c>OperatorMacroOverloadManagedTests</c> on the managed fallback.
/// </summary>
public static partial class OperatorMacroOverloadKernels
{
    [GpuKernel]
    public static void ShiftKernel(uint[] input, uint[] output, int bias)
    {
        ShiftCtx ctx = default;
        ctx.Bias = bias;
        ShiftRef x = input;
        ShiftRef r = output;
        r = x << 3;
    }

    [GpuKernel]
    public static void ReversedShiftKernel(uint[] input, uint[] output, int bias)
    {
        ShiftCtx ctx = default;
        ctx.Bias = bias;
        ReversedShiftRef x = input;
        ReversedShiftRef r = output;
        r = x << 3;
    }

    [GpuKernel]
    public static void PickKernel(uint[] left, uint[] right, uint[] output)
    {
        PickRef a = left;
        PickRef b = right;
        PickRef c = output;
        c = a + b;
    }

    // #641: the parentheses and the cast around the operator must not hide it; same call as c = a + b
    [GpuKernel]
    public static void CastPickKernel(uint[] left, uint[] right, uint[] output)
    {
        PickRef a = left;
        PickRef b = right;
        PickRef c = output;
        c = (PickRef)(a + b);
    }
}

// C# picks Shr(uint[], uint[], long) for an int shift: long converts implicitly to float and not back, so long is the
// better conversion target (C# spec §12.6.4.7). The float overload comes first and adds 1000 instead of shifting.
public static partial class ShrOps
{
    [GpuDeviceFunction]
    public static void Shr(uint[] result, uint[] value, float shift)
    {
        int tid = ThreadIndex.X;
        result[tid] = value[tid] + 1000u;
    }

    [GpuDeviceFunction]
    public static void Shr(uint[] result, uint[] value, long shift)
    {
        int tid = ThreadIndex.X;
        result[tid] = value[tid] >> (int)shift;
    }
}

public readonly ref struct ShrRef
{
    public readonly uint[] Data;

    public ShrRef(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(ShrOps), nameof(ShrOps.Shr))]
    public static ShrRef operator >>(ShrRef value, int shift) => throw new GpuOnlyException();

    public static implicit operator ShrRef(uint[] data) => new(data);

    public static implicit operator uint[](ShrRef value) => value.Data;
}

// The parameter types are the same; C# prefers the value parameter to the 'in' one. The 'in' overload comes first and
// adds 1000.
public static partial class SubOps
{
    [GpuDeviceFunction]
    public static void Sub(SubRef result, in SubRef left, SubRef right)
    {
        int tid = ThreadIndex.X;
        result.Data[tid] = left.Data[tid] - right.Data[tid] + 1000u;
    }

    [GpuDeviceFunction]
    public static void Sub(SubRef result, SubRef left, SubRef right)
    {
        int tid = ThreadIndex.X;
        result.Data[tid] = left.Data[tid] - right.Data[tid];
    }
}

public readonly ref struct SubRef
{
    public readonly uint[] Data;

    public SubRef(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(SubOps), nameof(SubOps.Sub))]
    public static SubRef operator -(SubRef left, SubRef right) => throw new GpuOnlyException();

    public static implicit operator SubRef(uint[] data) => new(data);
}

// The overload with a context takes CtxRef itself (identity conversions) and is better than the one without, which
// takes uint[] (decision V8 only makes the two ambiguous when neither is better)
public static partial class CtxOps
{
    [GpuDeviceFunction]
    public static void Add(uint[] result, uint[] left, uint[] right)
    {
        int tid = ThreadIndex.X;
        result[tid] = left[tid] + right[tid];
    }

    [GpuDeviceFunction]
    public static void Add(ref ShiftCtx ctx, CtxRef result, CtxRef left, CtxRef right)
    {
        int tid = ThreadIndex.X;
        result.Data[tid] = left.Data[tid] + right.Data[tid] + (uint)ctx.Bias;
    }
}

public readonly ref struct CtxRef
{
    public readonly uint[] Data;

    public CtxRef(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(CtxOps), nameof(CtxOps.Add))]
    public static CtxRef operator +(CtxRef left, CtxRef right) => throw new GpuOnlyException();

    public static implicit operator CtxRef(uint[] data) => new(data);

    public static implicit operator uint[](CtxRef value) => value.Data;
}

public static partial class OperatorMacroOverloadKernels
{
    [GpuKernel]
    public static void ShrKernel(uint[] input, uint[] output)
    {
        ShrRef x = input;
        ShrRef r = output;
        r = x >> 3;
    }

    [GpuKernel]
    public static void SubKernel(uint[] left, uint[] right, uint[] output)
    {
        SubRef a = left;
        SubRef b = right;
        SubRef c = output;
        c = a - b;
    }

    [GpuKernel]
    public static void ContextOverloadKernel(uint[] left, uint[] right, uint[] output, int bias)
    {
        ShiftCtx ctx = default;
        ctx.Bias = bias;
        CtxRef a = left;
        CtxRef b = right;
        CtxRef c = output;
        c = a + b;
    }
}

// The operator's shift is an int, which converts implicitly to long and not to byte, so only Shr(uint[], uint[], long)
// applies (#478). A call written with the constant 3 would take the byte overload instead (a constant converts to byte,
// the better conversion target), which comes first and adds 1000: the managed expansion passes the operand as a long
public static partial class ConstantShrOps
{
    [GpuDeviceFunction]
    public static void Shr(uint[] result, uint[] value, byte shift)
    {
        int tid = ThreadIndex.X;
        result[tid] = value[tid] + 1000u;
    }

    [GpuDeviceFunction]
    public static void Shr(uint[] result, uint[] value, long shift)
    {
        int tid = ThreadIndex.X;
        result[tid] = value[tid] >> (int)shift;
    }
}

public readonly ref struct ConstantShrRef
{
    public readonly uint[] Data;

    public ConstantShrRef(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(ConstantShrOps), nameof(ConstantShrOps.Shr))]
    public static ConstantShrRef operator >>(ConstantShrRef value, int shift) => throw new GpuOnlyException();

    public static implicit operator ConstantShrRef(uint[] data) => new(data);

    public static implicit operator uint[](ConstantShrRef value) => value.Data;
}

public static partial class OperatorMacroOverloadKernels
{
    [GpuKernel]
    public static void ConstantShrKernel(uint[] input, uint[] output)
    {
        ConstantShrRef x = input;
        ConstantShrRef r = output;
        r = x >> 3;
    }
}
