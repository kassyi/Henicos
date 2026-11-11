namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;
using Providers;

internal static class FloatVectorElementwiseHelper
{
    internal delegate GpuFloatResult UnaryElementOp(
        ref GpuBigIntExecutionContext context,
        IGpuBigFloatArithmetic arithmetic,
        in GpuFloatOperand source,
        GpuBigIntMemoryHandle destination);

    internal delegate GpuFloatResult BinaryElementOp(
        ref GpuBigIntExecutionContext context,
        IGpuBigFloatArithmetic arithmetic,
        in GpuFloatOperand left,
        in GpuFloatOperand right,
        GpuBigIntMemoryHandle destination);

    public static void ExecuteUnary(
        GpuWorkspace workspace,
        GpuBigFloatArrayView source,
        GpuBigFloatArrayView destination,
        int count,
        UnaryElementOp operation)
    {
        if (count == 0)
        {
            return;
        }

        var arithmetic = GpuWorkspaceArithmetic.FloatArithmetic(workspace);
        var execContext = GpuWorkspaceArithmetic.CreateContext(workspace);

        var srcExps = ArrayPool<uint>.Shared.Rent(count);
        var srcSigns = ArrayPool<uint>.Shared.Rent(count);
        var dstExps = ArrayPool<uint>.Shared.Rent(count);
        var dstSigns = ArrayPool<uint>.Shared.Rent(count);
        try
        {
            workspace.Provider.CopyDeviceToHost(srcExps, source.Exponents.DevicePointer, count, workspace.Stream);
            workspace.Provider.CopyDeviceToHost(srcSigns, source.Signs.DevicePointer, count, workspace.Stream);

            for (var i = 0; i < count; i++)
            {
                var srcSig = source.Significands.GetElement(i);
                var dstSig = destination.Significands.GetElement(i);
                var operand = new GpuFloatOperand(srcSig.Handle, unchecked((int)srcExps[i]), srcSigns[i] != 0);

                var res = operation(ref execContext, arithmetic, in operand, dstSig.Handle);
                dstExps[i] = unchecked((uint)res.Exponent);
                dstSigns[i] = res.IsNegative ? 1u : 0u;
            }

            workspace.Provider.CopyHostToDevice(destination.Exponents.DevicePointer, dstExps, count, workspace.Stream);
            workspace.Provider.CopyHostToDevice(destination.Signs.DevicePointer, dstSigns, count, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(srcExps);
            ArrayPool<uint>.Shared.Return(srcSigns);
            ArrayPool<uint>.Shared.Return(dstExps);
            ArrayPool<uint>.Shared.Return(dstSigns);
        }
    }

    public static void ExecuteBinary(
        GpuWorkspace workspace,
        GpuBigFloatArrayView left,
        GpuBigFloatArrayView right,
        GpuBigFloatArrayView destination,
        int count,
        BinaryElementOp operation)
    {
        if (count == 0)
        {
            return;
        }

        var arithmetic = GpuWorkspaceArithmetic.FloatArithmetic(workspace);
        var execContext = GpuWorkspaceArithmetic.CreateContext(workspace);

        var leftExps = ArrayPool<uint>.Shared.Rent(count);
        var leftSigns = ArrayPool<uint>.Shared.Rent(count);
        var rightExps = ArrayPool<uint>.Shared.Rent(count);
        var rightSigns = ArrayPool<uint>.Shared.Rent(count);
        var dstExps = ArrayPool<uint>.Shared.Rent(count);
        var dstSigns = ArrayPool<uint>.Shared.Rent(count);
        try
        {
            workspace.Provider.CopyDeviceToHost(leftExps, left.Exponents.DevicePointer, count, workspace.Stream);
            workspace.Provider.CopyDeviceToHost(leftSigns, left.Signs.DevicePointer, count, workspace.Stream);
            workspace.Provider.CopyDeviceToHost(rightExps, right.Exponents.DevicePointer, count, workspace.Stream);
            workspace.Provider.CopyDeviceToHost(rightSigns, right.Signs.DevicePointer, count, workspace.Stream);

            for (var i = 0; i < count; i++)
            {
                var leftSig = left.Significands.GetElement(i);
                var rightSig = right.Significands.GetElement(i);
                var dstSig = destination.Significands.GetElement(i);

                var leftOp = new GpuFloatOperand(leftSig.Handle, unchecked((int)leftExps[i]), leftSigns[i] != 0);
                var rightOp = new GpuFloatOperand(rightSig.Handle, unchecked((int)rightExps[i]), rightSigns[i] != 0);

                var res = operation(ref execContext, arithmetic, in leftOp, in rightOp, dstSig.Handle);
                dstExps[i] = unchecked((uint)res.Exponent);
                dstSigns[i] = res.IsNegative ? 1u : 0u;
            }

            workspace.Provider.CopyHostToDevice(destination.Exponents.DevicePointer, dstExps, count, workspace.Stream);
            workspace.Provider.CopyHostToDevice(destination.Signs.DevicePointer, dstSigns, count, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(leftExps);
            ArrayPool<uint>.Shared.Return(leftSigns);
            ArrayPool<uint>.Shared.Return(rightExps);
            ArrayPool<uint>.Shared.Return(rightSigns);
            ArrayPool<uint>.Shared.Return(dstExps);
            ArrayPool<uint>.Shared.Return(dstSigns);
        }
    }
}
