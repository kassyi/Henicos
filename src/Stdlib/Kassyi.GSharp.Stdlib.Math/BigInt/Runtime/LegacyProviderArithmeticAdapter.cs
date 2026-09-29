namespace Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

internal struct LegacyProviderArithmeticAdapter : IGpuBigIntArithmetic
{
    private static GpuUIntBigInteger Wrap(GpuWorkspace ctx, BigIntMemoryHandle handle)
    {
        return new GpuUIntBigInteger(ctx, handle, isTemp: true);
    }

    public void Add(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.Add(context.GpuWorkspace, Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void Sub(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.Sub(context.GpuWorkspace, Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void Mul(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.Mul(context.GpuWorkspace, Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void Div(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        GpuBigIntDivRem.Div(Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void Mod(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        GpuBigIntDivRem.Mod(Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void BitwiseAnd(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.BitwiseAnd(context.GpuWorkspace, Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void BitwiseOr(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.BitwiseOr(context.GpuWorkspace, Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void BitwiseXor(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.BitwiseXor(context.GpuWorkspace, Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void BitwiseNot(ref BigIntExecutionContext context, BigIntMemoryHandle operand, BigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.BitwiseNot(context.GpuWorkspace, Wrap(context.GpuWorkspace, operand), Wrap(context.GpuWorkspace, result));

    public void ShiftLeft(ref BigIntExecutionContext context, BigIntMemoryHandle operand, int shiftBits, BigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.ShiftLeft(context.GpuWorkspace, Wrap(context.GpuWorkspace, operand), shiftBits, Wrap(context.GpuWorkspace, result));

    public void ShiftRight(ref BigIntExecutionContext context, BigIntMemoryHandle operand, int shiftBits, BigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.ShiftRight(context.GpuWorkspace, Wrap(context.GpuWorkspace, operand), shiftBits, Wrap(context.GpuWorkspace, result));

    public void Sqrt(ref BigIntExecutionContext context, BigIntMemoryHandle operand, BigIntMemoryHandle result) =>
        GpuBigIntDivRem.Sqrt(Wrap(context.GpuWorkspace, operand), Wrap(context.GpuWorkspace, result));
}
