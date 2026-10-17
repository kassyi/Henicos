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
            ? NativeFloatProvider.Instance
            : GSharpKernelFloatProvider.Instance)
        : CSharpCpuBigFloatArithmetic.Instance);


    /// <summary>
    /// Evaluates the addition of two arbitrary-precision floating-point numbers on the GPU.
    /// </summary>
    /// <typeparam name="T">The concrete GPU big number type (<see cref="GpuBigFloat"/> or <see cref="GpuBigUFloat"/>).</typeparam>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>A new <typeparamref name="T"/> containing the sum.</returns>
    public T VisitAdd<T>(T left, T right)
        where T : struct, IGpuBigNumber<T>
        => ExecuteAddSub(left, right, negateRight: false);

    /// <summary>
    /// Evaluates the subtraction of two arbitrary-precision floating-point numbers on the GPU.
    /// </summary>
    /// <typeparam name="T">The concrete GPU big number type (<see cref="GpuBigFloat"/> or <see cref="GpuBigUFloat"/>).</typeparam>
    /// <param name="left">The left minuend operand.</param>
    /// <param name="right">The right subtrahend operand.</param>
    /// <returns>A new <typeparamref name="T"/> containing the difference.</returns>
    public T VisitSub<T>(T left, T right)
        where T : struct, IGpuBigNumber<T>
        => ExecuteAddSub(left, right, negateRight: true);

    private T ExecuteAddSub<T>(T left, T right, bool negateRight)
        where T : struct, IGpuBigNumber<T>
    {
        var maxWords = Math.Max(left.Magnitude.NumWords, right.Magnitude.NumWords) + 1;
        var resultSig = new GpuBigUInt(left.Workspace, maxWords);

        var execContext = new GpuBigIntExecutionContext(Workspace, Workspace.MemoryManager,
            new LegacyProviderArithmeticAdapter(), Workspace.Stream);
        var leftOp = GpuFloatOperand.From(left);
        var rightOp = GpuFloatOperand.From(right, negate: negateRight);

        var res = Arithmetic.Add(ref execContext, in leftOp, in rightOp, resultSig.Handle);

        return T.Create(resultSig, res.Exponent, left.CanBeNegative && res.IsNegative);
    }

    /// <summary>
    /// Evaluates the multiplication of two arbitrary-precision floating-point numbers on the GPU.
    /// </summary>
    /// <typeparam name="T">The concrete GPU big number type (<see cref="GpuBigFloat"/> or <see cref="GpuBigUFloat"/>).</typeparam>
    /// <param name="left">The left multiplicand operand.</param>
    /// <param name="right">The right multiplier operand.</param>
    /// <returns>A new <typeparamref name="T"/> containing the product.</returns>
    public T VisitMul<T>(T left, T right)
        where T : struct, IGpuBigNumber<T>
    {
        // Directly reuse GpuBigUInt AST for significands
        var sigExpr = left.Magnitude * right.Magnitude;
        var resultSig = sigExpr.Node.Evaluate(Workspace);

        var resultExp = left.Exponent + right.Exponent;
        var resultSign = left.CanBeNegative && !resultSig.IsZero() && (left.IsNegative != right.IsNegative);

        return T.Create(resultSig, resultExp, resultSign);
    }

    /// <summary>
    /// Evaluates the division of two arbitrary-precision floating-point numbers on the GPU.
    /// </summary>
    /// <typeparam name="T">The concrete GPU big number type (<see cref="GpuBigFloat"/> or <see cref="GpuBigUFloat"/>).</typeparam>
    /// <param name="left">The left dividend operand.</param>
    /// <param name="right">The right divisor operand.</param>
    /// <returns>A new <typeparamref name="T"/> containing the quotient.</returns>
    public T VisitDiv<T>(T left, T right)
        where T : struct, IGpuBigNumber<T>
    {
        var maxWords = Math.Max(left.Magnitude.NumWords, right.Magnitude.NumWords);
        var resultSig = new GpuBigUInt(left.Workspace, maxWords);

        try
        {
            var execContext = new GpuBigIntExecutionContext(Workspace, Workspace.MemoryManager,
                new LegacyProviderArithmeticAdapter(), Workspace.Stream);
            var leftOp = GpuFloatOperand.From(left);
            var rightOp = GpuFloatOperand.From(right);

            var res = Arithmetic.Divide(ref execContext, in leftOp, in rightOp, resultSig.Handle);

            return T.Create(resultSig, res.Exponent, left.CanBeNegative && res.IsNegative);
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
