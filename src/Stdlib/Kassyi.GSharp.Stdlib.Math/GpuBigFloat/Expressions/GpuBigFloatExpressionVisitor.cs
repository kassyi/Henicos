namespace Kassyi.GSharp.Stdlib.Math.GpuBigFloat.Expressions;

using System;
using Runtime;
using Kassyi.GSharp.Stdlib.Math.GpuBigInt.Runtime;
using GpuBigInt;
using Providers;

public ref struct GpuBigFloatExpressionVisitor(GpuWorkspace workspace, IGpuBigFloatArithmetic? arithmetic = null)
{
    public GpuWorkspace Workspace = workspace;

    public IGpuBigFloatArithmetic Arithmetic { get; } = arithmetic ?? (workspace.Engine == GpuExecutionEngine.GSharpAot
        ? GSharpAotFloatProvider.Instance
        : CSharpCpuBigFloatArithmetic.Instance);

    public GpuBigFloat VisitAdd(GpuBigFloat left, GpuBigFloat right)
    {
        var maxWords = Math.Max(left.UFloat.Significand.NumWords, right.UFloat.Significand.NumWords) + 1;
        var resultSig = new GpuUIntBigInteger(left.UFloat.Significand.Workspace, maxWords);

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

    public GpuBigFloat VisitSub(GpuBigFloat left, GpuBigFloat right)
    {
        var maxWords = Math.Max(left.UFloat.Significand.NumWords, right.UFloat.Significand.NumWords) + 1;
        var resultSig = new GpuUIntBigInteger(left.UFloat.Significand.Workspace, maxWords);

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

    public GpuBigFloat VisitMul(GpuBigFloat left, GpuBigFloat right)
    {
        // For multiplication, we can directly reuse GpuUIntBigInteger AST for significands!
        var sigExpr = left.UFloat.Significand * right.UFloat.Significand;
        var resultSig = GpuBigInt.Expressions.GpuBigUIntExpressionExtensions.Evaluate(sigExpr.Node, Workspace);

        var resultExp = left.UFloat.Exponent + right.UFloat.Exponent;
        var resultSign = left.IsNegative != right.IsNegative;

        return new GpuBigFloat(new GpuBigUFloat(resultSig, resultExp), resultSign);
    }

    public GpuBigUFloat VisitAdd(GpuBigUFloat left, GpuBigUFloat right)
    {
        var maxWords = Math.Max(left.Significand.NumWords, right.Significand.NumWords) + 1;
        var resultSig = new GpuUIntBigInteger(left.Significand.Workspace, maxWords);
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

    public GpuBigUFloat VisitSub(GpuBigUFloat left, GpuBigUFloat right)
    {
        var maxWords = Math.Max(left.Significand.NumWords, right.Significand.NumWords) + 1;
        var resultSig = new GpuUIntBigInteger(left.Significand.Workspace, maxWords);
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

    public GpuBigFloat Visit<TOp>(BinaryExpr<GpuBigFloat, TOp> expr)
        where TOp : struct, IFloatOperator<GpuBigFloat>
        => expr.Evaluate(ref this);

    public GpuBigUFloat Visit<TOp>(BinaryExpr<GpuBigUFloat, TOp> expr)
        where TOp : struct, IFloatOperator<GpuBigUFloat>
        => expr.Evaluate(ref this);
}
