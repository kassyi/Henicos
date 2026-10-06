namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Adapts the legacy <see cref="Providers.IGpuExecutionProvider"/> interface to the new <see cref="IGpuBigIntArithmetic"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Bridges older GPU execution providers and math kernels to the refactored execution context model
/// without breaking backward compatibility or requiring simultaneous rewrites of all backend providers.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> As a lightweight value struct wrapping static dispatch calls, no heap allocation occurs.
/// </para>
/// </remarks>
internal struct LegacyProviderArithmeticAdapter : IGpuBigIntArithmetic
{
    private static GpuBigUInt Wrap(GpuWorkspace ctx, GpuBigIntMemoryHandle handle)
    {
        return new GpuBigUInt(ctx, handle, isTemp: true);
    }

    /// <inheritdoc/>
    public void Add(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.Add(context.GpuWorkspace, Wrap(context.GpuWorkspace, left),
            Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    /// <inheritdoc/>
    public void Sub(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.Sub(context.GpuWorkspace, Wrap(context.GpuWorkspace, left),
            Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    /// <inheritdoc/>
    public void Mul(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.Mul(context.GpuWorkspace, Wrap(context.GpuWorkspace, left),
            Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    /// <inheritdoc/>
    public void Div(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        GpuBigIntDivRem.Div(Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right),
            Wrap(context.GpuWorkspace, result));

    /// <inheritdoc/>
    public void Mod(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        GpuBigIntDivRem.Mod(Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right),
            Wrap(context.GpuWorkspace, result));

    /// <inheritdoc/>
    public void BitwiseAnd(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.BitwiseAnd(context.GpuWorkspace, Wrap(context.GpuWorkspace, left),
            Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    /// <inheritdoc/>
    public void BitwiseOr(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.BitwiseOr(context.GpuWorkspace, Wrap(context.GpuWorkspace, left),
            Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    /// <inheritdoc/>
    public void BitwiseXor(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.BitwiseXor(context.GpuWorkspace, Wrap(context.GpuWorkspace, left),
            Wrap(context.GpuWorkspace, right), Wrap(context.GpuWorkspace, result));

    /// <inheritdoc/>
    public void BitwiseNot(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle operand,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.BitwiseNot(context.GpuWorkspace, Wrap(context.GpuWorkspace, operand),
            Wrap(context.GpuWorkspace, result));

    /// <inheritdoc/>
    public void ShiftLeft(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle operand, int shiftBits,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.ShiftLeft(context.GpuWorkspace, Wrap(context.GpuWorkspace, operand), shiftBits,
            Wrap(context.GpuWorkspace, result));

    /// <inheritdoc/>
    public void ShiftRight(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle operand, int shiftBits,
        GpuBigIntMemoryHandle result) =>
        context.GpuWorkspace.Provider.ShiftRight(context.GpuWorkspace, Wrap(context.GpuWorkspace, operand), shiftBits,
            Wrap(context.GpuWorkspace, result));

    /// <inheritdoc/>
    public void Sqrt(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle operand, GpuBigIntMemoryHandle result) =>
        GpuBigIntDivRem.Sqrt(Wrap(context.GpuWorkspace, operand), Wrap(context.GpuWorkspace, result));
}

