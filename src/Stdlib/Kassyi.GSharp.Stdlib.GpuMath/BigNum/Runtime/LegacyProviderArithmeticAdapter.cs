namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Runtime;

internal struct LegacyProviderArithmeticAdapter : IGpuBigIntArithmetic
{
    private static GpuBigUInt Wrap(GpuWorkspace ctx, GpuBigIntMemoryHandle handle)
    {
        return new GpuBigUInt(ctx, handle, isTemp: true);
    }

    public void Add(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.Add(context.GpuWorkspace, Wrap(context.GpuWorkspace, left),
            Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void Sub(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.Sub(context.GpuWorkspace, Wrap(context.GpuWorkspace, left),
            Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void Mul(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.Mul(context.GpuWorkspace, Wrap(context.GpuWorkspace, left),
            Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void Div(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        GpuBigIntDivRem.Div(Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right),
            Wrap(context.GpuWorkspace, result));

    public void Mod(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        GpuBigIntDivRem.Mod(Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right),
            Wrap(context.GpuWorkspace, result));

    public void BitwiseAnd(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.BitwiseAnd(context.GpuWorkspace, Wrap(context.GpuWorkspace, left),
            Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void BitwiseOr(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.BitwiseOr(context.GpuWorkspace, Wrap(context.GpuWorkspace, left),
            Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void BitwiseXor(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.BitwiseXor(context.GpuWorkspace, Wrap(context.GpuWorkspace, left),
            Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    public void BitwiseNot(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle operand,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.BitwiseNot(context.GpuWorkspace, Wrap(context.GpuWorkspace, operand),
            Wrap(context.GpuWorkspace, result));

    public void ShiftLeft(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle operand, int shiftBits,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.ShiftLeft(context.GpuWorkspace, Wrap(context.GpuWorkspace, operand), shiftBits,
            Wrap(context.GpuWorkspace, result));

    public void ShiftRight(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle operand, int shiftBits,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.ShiftRight(context.GpuWorkspace, Wrap(context.GpuWorkspace, operand), shiftBits,
            Wrap(context.GpuWorkspace, result));

    public void Sqrt(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle operand, GpuBigIntMemoryHandle result) =>
        GpuBigIntDivRem.Sqrt(Wrap(context.GpuWorkspace, operand), Wrap(context.GpuWorkspace, result));
}
