namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;
using BigNum;
using Providers;

/// <summary>
/// A stack-scoped visitor that evaluates arbitrary-precision floating-point AST expressions across GPU and CPU backends.
/// </summary>
/// <param name="workspace">The GPU workspace managing kernel parameters and memory pools.</param>
/// <param name="arithmetic">The optional arithmetic provider instance (falls back to workspace default if null).</param>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Bridges generic math expressions (<see cref="BinaryExpr{T, TOp}"/>) to device-level mantissa and exponent arithmetic.
/// </para>
/// </remarks>
public ref struct GpuBigFloatExpressionVisitor(GpuWorkspace workspace, IGpuBigFloatArithmetic? arithmetic = null)
{
    /// <summary>
    /// The parent GPU workspace.
    /// </summary>
    public GpuWorkspace Workspace = workspace;

    /// <summary>
    /// Gets the active arbitrary-precision float arithmetic provider.
    /// </summary>
    public IGpuBigFloatArithmetic Arithmetic
    {
        get;
    } = arithmetic ?? (workspace.Engine == GpuExecutionEngine.GSharpAot
        ? (workspace.Provider is NativeProvider
            ? GSharpAotFloatProvider.CreateNativeCuda()
            : GSharpAotFloatProvider.CreateGSharpKernel())
        : CSharpCpuBigFloatArithmetic.Instance);


    /// <summary>
    /// Evaluates the addition of two signed arbitrary-precision floating-point numbers on the GPU.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>A new <see cref="GpuBigFloat"/> containing the sum.</returns>
    public GpuBigFloat VisitAdd(GpuBigFloat left, GpuBigFloat right)
    {
        var maxWords = Math.Max(left.UFloat.Significand.NumWords, right.UFloat.Significand.NumWords) + 1;
        var resultSig = new GpuBigUInt(left.UFloat.Significand.Workspace, maxWords);

        var execContext = new GpuBigIntExecutionContext(Workspace, Workspace.MemoryManager,
            new LegacyProviderArithmeticAdapter(), Workspace.Stream);
        Arithmetic.Add(ref execContext,
            new GpuBigIntMemoryHandle(
                new GpuBigIntDeviceBuffer(left.UFloat.Significand.DevicePointer, left.UFloat.Significand.NumWords), 0,
                left.UFloat.Significand.NumWords), left.UFloat.Exponent, left.IsNegative,
            new GpuBigIntMemoryHandle(
                new GpuBigIntDeviceBuffer(right.UFloat.Significand.DevicePointer, right.UFloat.Significand.NumWords), 0,
                right.UFloat.Significand.NumWords), right.UFloat.Exponent, right.IsNegative,
            new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resultSig.DevicePointer, resultSig.NumWords), 0,
                resultSig.NumWords), out var resultExp, out var resultSign);

        return new GpuBigFloat(new GpuBigUFloat(resultSig, resultExp), resultSign);
    }

    /// <summary>
    /// Evaluates the subtraction of two signed arbitrary-precision floating-point numbers on the GPU.
    /// </summary>
    /// <param name="left">The left minuend operand.</param>
    /// <param name="right">The right subtrahend operand.</param>
    /// <returns>A new <see cref="GpuBigFloat"/> containing the difference.</returns>
    public GpuBigFloat VisitSub(GpuBigFloat left, GpuBigFloat right)
    {
        var maxWords = Math.Max(left.UFloat.Significand.NumWords, right.UFloat.Significand.NumWords) + 1;
        var resultSig = new GpuBigUInt(left.UFloat.Significand.Workspace, maxWords);

        var execContext = new GpuBigIntExecutionContext(Workspace, Workspace.MemoryManager,
            new LegacyProviderArithmeticAdapter(), Workspace.Stream);

        // Subtraction is simply addition with the right sign flipped
        Arithmetic.Add(ref execContext,
            new GpuBigIntMemoryHandle(
                new GpuBigIntDeviceBuffer(left.UFloat.Significand.DevicePointer, left.UFloat.Significand.NumWords), 0,
                left.UFloat.Significand.NumWords), left.UFloat.Exponent, left.IsNegative,
            new GpuBigIntMemoryHandle(
                new GpuBigIntDeviceBuffer(right.UFloat.Significand.DevicePointer, right.UFloat.Significand.NumWords), 0,
                right.UFloat.Significand.NumWords), right.UFloat.Exponent, !right.IsNegative,
            new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resultSig.DevicePointer, resultSig.NumWords), 0,
                resultSig.NumWords), out var resultExp, out var resultSign);

        return new GpuBigFloat(new GpuBigUFloat(resultSig, resultExp), resultSign);
    }

    /// <summary>
    /// Evaluates the multiplication of two signed arbitrary-precision floating-point numbers on the GPU.
    /// </summary>
    /// <param name="left">The left multiplicand operand.</param>
    /// <param name="right">The right multiplier operand.</param>
    /// <returns>A new <see cref="GpuBigFloat"/> containing the product.</returns>
    public GpuBigFloat VisitMul(GpuBigFloat left, GpuBigFloat right)
    {
        // For multiplication, we can directly reuse GpuBigUInt AST for significands!
        var sigExpr = left.UFloat.Significand * right.UFloat.Significand;
        var resultSig = sigExpr.Node.Evaluate(Workspace);

        var resultExp = left.UFloat.Exponent + right.UFloat.Exponent;
        var resultSign = !resultSig.IsZero() && (left.IsNegative != right.IsNegative);

        return new GpuBigFloat(new GpuBigUFloat(resultSig, resultExp), resultSign);
    }

    /// <summary>
    /// Evaluates the division of two signed arbitrary-precision floating-point numbers on the GPU.
    /// </summary>
    /// <param name="left">The left dividend operand.</param>
    /// <param name="right">The right divisor operand.</param>
    /// <returns>A new <see cref="GpuBigFloat"/> containing the quotient.</returns>
    public GpuBigFloat VisitDiv(GpuBigFloat left, GpuBigFloat right)
    {
        var maxWords = Math.Max(left.UFloat.Significand.NumWords, right.UFloat.Significand.NumWords);
        var resultSig = new GpuBigUInt(left.UFloat.Significand.Workspace, maxWords);

        try
        {
            var execContext = new GpuBigIntExecutionContext(Workspace, Workspace.MemoryManager,
                new LegacyProviderArithmeticAdapter(), Workspace.Stream);
            Arithmetic.Divide(ref execContext,
                new GpuBigIntMemoryHandle(
                    new GpuBigIntDeviceBuffer(left.UFloat.Significand.DevicePointer, left.UFloat.Significand.NumWords), 0,
                    left.UFloat.Significand.NumWords), left.UFloat.Exponent, left.IsNegative,
                new GpuBigIntMemoryHandle(
                    new GpuBigIntDeviceBuffer(right.UFloat.Significand.DevicePointer, right.UFloat.Significand.NumWords), 0,
                    right.UFloat.Significand.NumWords), right.UFloat.Exponent, right.IsNegative,
                new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resultSig.DevicePointer, resultSig.NumWords), 0,
                    resultSig.NumWords), out var resultExp, out var resultSign);

            return new GpuBigFloat(new GpuBigUFloat(resultSig, resultExp), resultSign);
        }
        catch
        {
            resultSig.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Evaluates the addition of two unsigned arbitrary-precision floating-point numbers on the GPU.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>A new <see cref="GpuBigUFloat"/> containing the sum.</returns>
    public GpuBigUFloat VisitAdd(GpuBigUFloat left, GpuBigUFloat right)
    {
        var maxWords = Math.Max(left.Significand.NumWords, right.Significand.NumWords) + 1;
        var resultSig = new GpuBigUInt(left.Significand.Workspace, maxWords);
        var execContext = new GpuBigIntExecutionContext(Workspace, Workspace.MemoryManager,
            new LegacyProviderArithmeticAdapter(), Workspace.Stream);
        Arithmetic.Add(ref execContext,
            new GpuBigIntMemoryHandle(
                new GpuBigIntDeviceBuffer(left.Significand.DevicePointer, left.Significand.NumWords),
                0, left.Significand.NumWords), left.Exponent, false,
            new GpuBigIntMemoryHandle(
                new GpuBigIntDeviceBuffer(right.Significand.DevicePointer, right.Significand.NumWords), 0,
                right.Significand.NumWords), right.Exponent, false,
            new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resultSig.DevicePointer, resultSig.NumWords), 0,
                resultSig.NumWords), out var resultExp, out var _);
        return new GpuBigUFloat(resultSig, resultExp);
    }

    /// <summary>
    /// Evaluates the subtraction of two unsigned arbitrary-precision floating-point numbers on the GPU.
    /// </summary>
    /// <param name="left">The left minuend operand.</param>
    /// <param name="right">The right subtrahend operand.</param>
    /// <returns>A new <see cref="GpuBigUFloat"/> containing the difference.</returns>
    public GpuBigUFloat VisitSub(GpuBigUFloat left, GpuBigUFloat right)
    {
        var maxWords = Math.Max(left.Significand.NumWords, right.Significand.NumWords) + 1;
        var resultSig = new GpuBigUInt(left.Significand.Workspace, maxWords);
        var execContext = new GpuBigIntExecutionContext(Workspace, Workspace.MemoryManager,
            new LegacyProviderArithmeticAdapter(), Workspace.Stream);
        Arithmetic.Add(ref execContext,
            new GpuBigIntMemoryHandle(
                new GpuBigIntDeviceBuffer(left.Significand.DevicePointer, left.Significand.NumWords),
                0, left.Significand.NumWords), left.Exponent, false,
            new GpuBigIntMemoryHandle(
                new GpuBigIntDeviceBuffer(right.Significand.DevicePointer, right.Significand.NumWords), 0,
                right.Significand.NumWords), right.Exponent, true,
            new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resultSig.DevicePointer, resultSig.NumWords), 0,
                resultSig.NumWords), out var resultExp, out var _);
        return new GpuBigUFloat(resultSig, resultExp);
    }

    /// <summary>
    /// Evaluates the multiplication of two unsigned arbitrary-precision floating-point numbers on the GPU.
    /// </summary>
    /// <param name="left">The left multiplicand operand.</param>
    /// <param name="right">The right multiplier operand.</param>
    /// <returns>A new <see cref="GpuBigUFloat"/> containing the product.</returns>
    public GpuBigUFloat VisitMul(GpuBigUFloat left, GpuBigUFloat right)
    {
        var sigExpr = left.Significand * right.Significand;
        var resultSig = sigExpr.Node.Evaluate(Workspace);
        var resultExp = left.Exponent + right.Exponent;
        return new GpuBigUFloat(resultSig, resultExp);
    }

    /// <summary>
    /// Evaluates the division of two unsigned arbitrary-precision floating-point numbers on the GPU.
    /// </summary>
    /// <param name="left">The left dividend operand.</param>
    /// <param name="right">The right divisor operand.</param>
    /// <returns>A new <see cref="GpuBigUFloat"/> containing the quotient.</returns>
    public GpuBigUFloat VisitDiv(GpuBigUFloat left, GpuBigUFloat right)
    {
        var maxWords = Math.Max(left.Significand.NumWords, right.Significand.NumWords);
        var resultSig = new GpuBigUInt(left.Significand.Workspace, maxWords);

        try
        {
            var execContext = new GpuBigIntExecutionContext(Workspace, Workspace.MemoryManager,
                new LegacyProviderArithmeticAdapter(), Workspace.Stream);
            Arithmetic.Divide(ref execContext,
                new GpuBigIntMemoryHandle(
                    new GpuBigIntDeviceBuffer(left.Significand.DevicePointer, left.Significand.NumWords),
                    0, left.Significand.NumWords), left.Exponent, false,
                new GpuBigIntMemoryHandle(
                    new GpuBigIntDeviceBuffer(right.Significand.DevicePointer, right.Significand.NumWords), 0,
                    right.Significand.NumWords), right.Exponent, false,
                new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resultSig.DevicePointer, resultSig.NumWords), 0,
                    resultSig.NumWords), out var resultExp, out var _);

            return new GpuBigUFloat(resultSig, resultExp);
        }
        catch
        {
            resultSig.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Evaluates a binary operator node on signed arbitrary-precision floating-point expressions.
    /// </summary>
    /// <typeparam name="TOp">The binary operator struct type.</typeparam>
    /// <param name="expr">The binary expression node.</param>
    /// <returns>The evaluated <see cref="GpuBigFloat"/> result.</returns>
    public GpuBigFloat Visit<TOp>(BinaryExpr<GpuBigFloat, TOp> expr)
        where TOp : struct, IFloatOperator<GpuBigFloat>
        => expr.Evaluate(ref this);

    /// <summary>
    /// Evaluates a binary operator node on unsigned arbitrary-precision floating-point expressions.
    /// </summary>
    /// <typeparam name="TOp">The binary operator struct type.</typeparam>
    /// <param name="expr">The binary expression node.</param>
    /// <returns>The evaluated <see cref="GpuBigUFloat"/> result.</returns>
    public GpuBigUFloat Visit<TOp>(BinaryExpr<GpuBigUFloat, TOp> expr)
        where TOp : struct, IFloatOperator<GpuBigUFloat>
        => expr.Evaluate(ref this);
}
