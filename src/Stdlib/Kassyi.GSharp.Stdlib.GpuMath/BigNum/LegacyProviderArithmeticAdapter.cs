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
/// <b>Widths:</b> the operands and the result may each have their own word count; the result is exact when it is as
/// wide as the bound of the operation (<see cref="BigIntWidths"/>), which the expression nodes guarantee. A difference
/// below zero is a domain error.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> As a lightweight value struct wrapping static dispatch calls, no heap allocation occurs.
/// </para>
/// </remarks>
internal struct LegacyProviderArithmeticAdapter : IGpuBigIntArithmetic
{
    // A borrowed view: the handle belongs to the caller's scoped memory, so disposing the wrapper must not free it
    private static GpuBigUInt Wrap(GpuWorkspace ctx, GpuBigIntMemoryHandle handle) => new(ctx, handle.Borrow());

    /// <inheritdoc/>
    public void Add(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        BigIntWideOps.AddSubtract(Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right),
            Wrap(context.GpuWorkspace, result), subtract: false);

    /// <inheritdoc/>
    public void Sub(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        BigIntWideOps.AddSubtract(Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right),
            Wrap(context.GpuWorkspace, result), subtract: true);

    /// <inheritdoc/>
    public void Mul(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result)
    {
        var workspace = context.GpuWorkspace;
        var resultBuffer = Wrap(workspace, result);

        // The product kernels write the sum of the operand words; a result narrower than that (a bound below the words
        // of the operands) gets the product through a temporary whose top words are zero
        var productWords = left.NumWords + right.NumWords;
        if (result.NumWords == productWords)
        {
            workspace.Provider.Mul(workspace, Wrap(workspace, left), Wrap(workspace, right), resultBuffer);
            return;
        }

        using var product = workspace.RentTemp(productWords);
        workspace.Provider.Mul(workspace, Wrap(workspace, left), Wrap(workspace, right), product);
        resultBuffer.CopyFrom(product, numWords: Math.Min(result.NumWords, productWords));
        ResultBuffers.ClearAbove(resultBuffer, Math.Min(result.NumWords, productWords));
    }

    /// <inheritdoc/>
    public void Div(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result)
    {
        var workspace = context.GpuWorkspace;
        using var remainder = workspace.RentTemp(right.NumWords);
        GpuBigIntDivRem.DivRem(Wrap(workspace, left), Wrap(workspace, right), Wrap(workspace, result), remainder);
    }

    /// <inheritdoc/>
    public void Mod(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result)
    {
        // The remainder is below the divisor and at most the dividend, so the low words of the divisor-wide remainder
        // hold it
        var workspace = context.GpuWorkspace;
        using var quotient = workspace.RentTemp(left.NumWords);
        using var remainder = workspace.RentTemp(right.NumWords);
        GpuBigIntDivRem.DivRem(Wrap(workspace, left), Wrap(workspace, right), quotient, remainder);
        var resultBuffer = Wrap(workspace, result);
        var copied = Math.Min(result.NumWords, right.NumWords);
        resultBuffer.CopyFrom(remainder, numWords: copied);
        ResultBuffers.ClearAbove(resultBuffer, copied);
    }

    /// <inheritdoc/>
    public void BitwiseAnd(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        BigIntWideOps.Bitwise(Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right),
            Wrap(context.GpuWorkspace, result), GpuBigIntWideKernels.BitwiseAnd);

    /// <inheritdoc/>
    public void BitwiseOr(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        BigIntWideOps.Bitwise(Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right),
            Wrap(context.GpuWorkspace, result), GpuBigIntWideKernels.BitwiseOr);

    /// <inheritdoc/>
    public void BitwiseXor(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result) =>
        BigIntWideOps.Bitwise(Wrap(context.GpuWorkspace, left), Wrap(context.GpuWorkspace, right),
            Wrap(context.GpuWorkspace, result), GpuBigIntWideKernels.BitwiseXor);

    /// <inheritdoc/>
    public void BitwiseNot(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle operand,
        GpuBigIntMemoryHandle result)
    {
        var a = Wrap(context.GpuWorkspace, operand);
        BigIntWideOps.Bitwise(a, a, Wrap(context.GpuWorkspace, result), GpuBigIntWideKernels.BitwiseNot);
    }

    /// <inheritdoc/>
    /// <remarks>A negative <paramref name="shiftBits"/> shifts right, as <see cref="BigInteger"/>.</remarks>
    public void ShiftLeft(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle operand, int shiftBits,
        GpuBigIntMemoryHandle result) =>
        Shift(context.GpuWorkspace, operand, shiftBits, result);

    /// <inheritdoc/>
    /// <remarks>A negative <paramref name="shiftBits"/> shifts left, as <see cref="BigInteger"/>.</remarks>
    public void ShiftRight(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle operand, int shiftBits,
        GpuBigIntMemoryHandle result) =>
        Shift(context.GpuWorkspace, operand, -(long)shiftBits, result);

    /// <inheritdoc/>
    public void Sqrt(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle operand, GpuBigIntMemoryHandle result)
    {
        // The root has half the bits of the operand; the square root kernels write the operand's width, so a narrower
        // result gets it through a temporary
        var workspace = context.GpuWorkspace;
        var resultBuffer = Wrap(workspace, result);
        if (result.NumWords == operand.NumWords)
        {
            GpuBigIntDivRem.Sqrt(Wrap(workspace, operand), resultBuffer);
            return;
        }

        using var root = workspace.RentTemp(operand.NumWords);
        GpuBigIntDivRem.Sqrt(Wrap(workspace, operand), root);
        var copied = Math.Min(result.NumWords, operand.NumWords);
        resultBuffer.CopyFrom(root, numWords: copied);
        ResultBuffers.ClearAbove(resultBuffer, copied);
    }

    // operand * 2^leftShift into result (a negative leftShift shifts right); the kernel never sees a negative count
    private static void Shift(GpuWorkspace workspace, GpuBigIntMemoryHandle operand, long leftShift,
        GpuBigIntMemoryHandle result)
    {
        var resultBuffer = Wrap(workspace, result);
        if (leftShift <= -(long)BigIntWidths.BitsPerWord * operand.NumWords)
        {
            // Every bit leaves the operand: zero
            workspace.Provider.ClearMemoryAsync(resultBuffer.DevicePointer, resultBuffer.NumWords, workspace.Stream);
            return;
        }

        if (leftShift > int.MaxValue)
        {
            // A right shift by int.MinValue is a left shift by 2^31, one more than an int holds: two steps, both into
            // the result, which is as wide as the bound
            GpuBigIntMath.ShiftAndResize(Wrap(workspace, operand), int.MaxValue, resultBuffer);
            GpuBigIntMath.ShiftAndResize(resultBuffer, (int)(leftShift - int.MaxValue), resultBuffer);
            return;
        }

        GpuBigIntMath.ShiftAndResize(Wrap(workspace, operand), (int)leftShift, resultBuffer);
    }
}
