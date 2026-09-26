namespace Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

using Providers;

internal struct LegacyProviderArithmeticAdapter : IGpuBigIntArithmetic
{
    private static GpuUIntBigInteger Wrap(GpuContext ctx, BigIntMemoryHandle handle) =>
        new GpuUIntBigInteger(ctx, handle.NumWords, handle.Buffer.Pointer) { IsTemp = true };

    public void Add(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        context.GpuContext.Provider.Add(context.GpuContext, Wrap(context.GpuContext, left), Wrap(context.GpuContext, right), Wrap(context.GpuContext, result));

    public void Sub(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        context.GpuContext.Provider.Sub(context.GpuContext, Wrap(context.GpuContext, left), Wrap(context.GpuContext, right), Wrap(context.GpuContext, result));

    public void Mul(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        context.GpuContext.Provider.Mul(context.GpuContext, Wrap(context.GpuContext, left), Wrap(context.GpuContext, right), Wrap(context.GpuContext, result));

    public void Div(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        GpuBigIntDivRem.Div(Wrap(context.GpuContext, left), Wrap(context.GpuContext, right), Wrap(context.GpuContext, result));

    public void Mod(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        GpuBigIntDivRem.Mod(Wrap(context.GpuContext, left), Wrap(context.GpuContext, right), Wrap(context.GpuContext, result));

    public void BitwiseAnd(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        context.GpuContext.Provider.BitwiseAnd(context.GpuContext, Wrap(context.GpuContext, left), Wrap(context.GpuContext, right), Wrap(context.GpuContext, result));

    public void BitwiseOr(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        context.GpuContext.Provider.BitwiseOr(context.GpuContext, Wrap(context.GpuContext, left), Wrap(context.GpuContext, right), Wrap(context.GpuContext, result));

    public void BitwiseXor(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        context.GpuContext.Provider.BitwiseXor(context.GpuContext, Wrap(context.GpuContext, left), Wrap(context.GpuContext, right), Wrap(context.GpuContext, result));

    public void BitwiseNot(ref BigIntExecutionContext context, BigIntMemoryHandle operand, BigIntMemoryHandle result) =>
        context.GpuContext.Provider.BitwiseNot(context.GpuContext, Wrap(context.GpuContext, operand), Wrap(context.GpuContext, result));

    public void ShiftLeft(ref BigIntExecutionContext context, BigIntMemoryHandle operand, int shiftBits, BigIntMemoryHandle result) =>
        context.GpuContext.Provider.ShiftLeft(context.GpuContext, Wrap(context.GpuContext, operand), shiftBits, Wrap(context.GpuContext, result));

    public void ShiftRight(ref BigIntExecutionContext context, BigIntMemoryHandle operand, int shiftBits, BigIntMemoryHandle result) =>
        context.GpuContext.Provider.ShiftRight(context.GpuContext, Wrap(context.GpuContext, operand), shiftBits, Wrap(context.GpuContext, result));

    public void Sqrt(ref BigIntExecutionContext context, BigIntMemoryHandle operand, BigIntMemoryHandle result) =>
        GpuBigIntDivRem.Sqrt(Wrap(context.GpuContext, operand), Wrap(context.GpuContext, result));
}