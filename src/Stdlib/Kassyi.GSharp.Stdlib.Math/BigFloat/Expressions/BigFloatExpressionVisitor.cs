namespace Kassyi.GSharp.Stdlib.Math.BigFloat.Expressions;

using System;
using Runtime;
using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;
using BigInt;
using Providers;

public ref struct BigFloatExpressionVisitor(GpuWorkspace workspace, IGpuBigFloatArithmetic? arithmetic = null)
{
    public GpuWorkspace Workspace = workspace;
    public IGpuBigFloatArithmetic Arithmetic { get; } = arithmetic ?? (workspace.Engine == GpuExecutionEngine.GSharpAot

        ? GSharpAotFloatProvider.Instance

        : CSharpBigFloatArithmetic.Instance);

    public GpuBigFloat VisitAdd(GpuBigFloatAddExpr expr)
    {
        var left = expr.Left;
        var right = expr.Right;
        
        var maxWords = Math.Max(left.UFloat.Significand.NumWords, right.UFloat.Significand.NumWords) + 1;
        var resultSig = new GpuUIntBigInteger(left.UFloat.Significand.Workspace, maxWords);
        
        var execContext = new BigIntExecutionContext(Workspace, Workspace.MemoryManager, new LegacyProviderArithmeticAdapter(), Workspace.Stream);
        Arithmetic.Add(ref execContext,
            new BigIntMemoryHandle(new BigIntDeviceBuffer(left.UFloat.Significand.DevicePointer, left.UFloat.Significand.NumWords), 0, left.UFloat.Significand.NumWords), left.UFloat.Exponent, left.IsNegative,
            new BigIntMemoryHandle(new BigIntDeviceBuffer(right.UFloat.Significand.DevicePointer, right.UFloat.Significand.NumWords), 0, right.UFloat.Significand.NumWords), right.UFloat.Exponent, right.IsNegative,
            new BigIntMemoryHandle(new BigIntDeviceBuffer(resultSig.DevicePointer, resultSig.NumWords), 0, resultSig.NumWords), out var resultExp, out var resultSign);
            
        return new GpuBigFloat(new GpuBigUFloat(resultSig, resultExp), resultSign);
    }
    
    public GpuBigFloat VisitSub(GpuBigFloatSubExpr expr)
    {
        var left = expr.Left;
        var right = expr.Right;
        
        var maxWords = Math.Max(left.UFloat.Significand.NumWords, right.UFloat.Significand.NumWords) + 1;
        var resultSig = new GpuUIntBigInteger(left.UFloat.Significand.Workspace, maxWords);
        
        var execContext = new BigIntExecutionContext(Workspace, Workspace.MemoryManager, new LegacyProviderArithmeticAdapter(), Workspace.Stream);
        
        // Subtraction is simply addition with the right sign flipped
        Arithmetic.Add(ref execContext,
            new BigIntMemoryHandle(new BigIntDeviceBuffer(left.UFloat.Significand.DevicePointer, left.UFloat.Significand.NumWords), 0, left.UFloat.Significand.NumWords), left.UFloat.Exponent, left.IsNegative,
            new BigIntMemoryHandle(new BigIntDeviceBuffer(right.UFloat.Significand.DevicePointer, right.UFloat.Significand.NumWords), 0, right.UFloat.Significand.NumWords), right.UFloat.Exponent, !right.IsNegative,
            new BigIntMemoryHandle(new BigIntDeviceBuffer(resultSig.DevicePointer, resultSig.NumWords), 0, resultSig.NumWords), out var resultExp, out var resultSign);
            
        return new GpuBigFloat(new GpuBigUFloat(resultSig, resultExp), resultSign);
    }

    public GpuBigFloat VisitMul(GpuBigFloatMulExpr expr)
    {
        var left = expr.Left;
        var right = expr.Right;
        
        // For multiplication, we can directly reuse GpuUIntBigInteger AST for significands!
        var sigExpr = left.UFloat.Significand * right.UFloat.Significand;
        var resultSig = BigInt.Expressions.GpuBigUIntExpressionExtensions.Evaluate(sigExpr.Node, Workspace);
        
        var resultExp = left.UFloat.Exponent + right.UFloat.Exponent;
        var resultSign = left.IsNegative != right.IsNegative;
        
        return new GpuBigFloat(new GpuBigUFloat(resultSig, resultExp), resultSign);
    }
    
    public GpuBigUFloat VisitAdd(GpuBigUFloatAddExpr expr)
    {
        var left = expr.Left;
        var right = expr.Right;
        var maxWords = Math.Max(left.Significand.NumWords, right.Significand.NumWords) + 1;
        var resultSig = new GpuUIntBigInteger(left.Significand.Workspace, maxWords);
        var execContext = new BigIntExecutionContext(Workspace, Workspace.MemoryManager, new LegacyProviderArithmeticAdapter(), Workspace.Stream);
        Arithmetic.Add(ref execContext,
            new BigIntMemoryHandle(new BigIntDeviceBuffer(left.Significand.DevicePointer, left.Significand.NumWords), 0, left.Significand.NumWords), left.Exponent, false,
            new BigIntMemoryHandle(new BigIntDeviceBuffer(right.Significand.DevicePointer, right.Significand.NumWords), 0, right.Significand.NumWords), right.Exponent, false,
            new BigIntMemoryHandle(new BigIntDeviceBuffer(resultSig.DevicePointer, resultSig.NumWords), 0, resultSig.NumWords), out var resultExp, out var _);
        return new GpuBigUFloat(resultSig, resultExp);
    }

    public GpuBigUFloat VisitSub(GpuBigUFloatSubExpr expr)
    {
        var left = expr.Left;
        var right = expr.Right;
        var maxWords = Math.Max(left.Significand.NumWords, right.Significand.NumWords) + 1;
        var resultSig = new GpuUIntBigInteger(left.Significand.Workspace, maxWords);
        var execContext = new BigIntExecutionContext(Workspace, Workspace.MemoryManager, new LegacyProviderArithmeticAdapter(), Workspace.Stream);
        Arithmetic.Add(ref execContext,
            new BigIntMemoryHandle(new BigIntDeviceBuffer(left.Significand.DevicePointer, left.Significand.NumWords), 0, left.Significand.NumWords), left.Exponent, false,
            new BigIntMemoryHandle(new BigIntDeviceBuffer(right.Significand.DevicePointer, right.Significand.NumWords), 0, right.Significand.NumWords), right.Exponent, true,
            new BigIntMemoryHandle(new BigIntDeviceBuffer(resultSig.DevicePointer, resultSig.NumWords), 0, resultSig.NumWords), out var resultExp, out var _);
        return new GpuBigUFloat(resultSig, resultExp);
    }
}
