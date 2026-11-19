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
        using var src = HostWords.Read(workspace, source, count);
        using var dst = HostWords.Rent(count);
        var i = 0;
        for (; i < count; i++)
        {
            var operand = src.Operand(source, i);
            var target = destination.Significands.GetElement(i).Handle;
            try
            {
                var result = operation(ref execContext, arithmetic, in operand, target);
                dst.Store(i, result);
            }
            catch (Exception ex) when (IsDeferred(ex))
            {
                PendingThrows.TryRecord(i, ex);
                break;
            }
        }

        dst.Write(workspace, destination, count);
        RecordedTail.Clear(workspace, destination, i, count);
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
        using var lhs = HostWords.Read(workspace, left, count);
        using var rhs = HostWords.Read(workspace, right, count);
        using var dst = HostWords.Rent(count);
        var i = 0;
        for (; i < count; i++)
        {
            var leftOp = lhs.Operand(left, i);
            var rightOp = rhs.Operand(right, i);
            var target = destination.Significands.GetElement(i).Handle;
            try
            {
                var result = operation(ref execContext, arithmetic, in leftOp, in rightOp, target);
                dst.Store(i, result);
            }
            catch (Exception ex) when (IsDeferred(ex))
            {
                PendingThrows.TryRecord(i, ex);
                break;
            }
        }

        dst.Write(workspace, destination, count);
        RecordedTail.Clear(workspace, destination, i, count);
    }

    // Under a PendingThrows scope (Any / All), the exception of an element value is recorded and the loop stops: the
    // elements from it on are not computed, and RecordedTail sets them to zero after the write-back. Any other
    // exception, or one outside a scope, propagates. The filter only reads: the record is made in the handler.
    private static bool IsDeferred(Exception exception) =>
        PendingThrows.IsActive && PendingThrows.IsElementException(exception);

    // The exponents and signs of the elements of a float vector on the host, in arrays rented from the shared pool
    private readonly ref struct HostWords
    {
        private readonly uint[] _exponents;
        private readonly uint[] _signs;

        private HostWords(int count)
        {
            _exponents = ArrayPool<uint>.Shared.Rent(count);
            _signs = ArrayPool<uint>.Shared.Rent(count);
        }

        public static HostWords Rent(int count) => new(count);

        public static HostWords Read(GpuWorkspace workspace, GpuBigFloatArrayView view, int count)
        {
            var words = new HostWords(count);
            var stream = workspace.Stream;
            workspace.Provider.CopyDeviceToHost(words._exponents, view.Exponents.DevicePointer, count, stream);
            workspace.Provider.CopyDeviceToHost(words._signs, view.Signs.DevicePointer, count, stream);
            return words;
        }

        public GpuFloatOperand Operand(GpuBigFloatArrayView view, int index) =>
            new(view.Significands.GetElement(index).Handle, unchecked((int)_exponents[index]), _signs[index] != 0);

        public void Store(int index, GpuFloatResult result)
        {
            _exponents[index] = unchecked((uint)result.Exponent);
            _signs[index] = result.IsNegative ? 1u : 0u;
        }

        public void Write(GpuWorkspace workspace, GpuBigFloatArrayView view, int count)
        {
            workspace.Provider.CopyHostToDevice(view.Exponents.DevicePointer, _exponents, count, workspace.Stream);
            workspace.Provider.CopyHostToDevice(view.Signs.DevicePointer, _signs, count, workspace.Stream);
        }

        public void Dispose()
        {
            ArrayPool<uint>.Shared.Return(_exponents);
            ArrayPool<uint>.Shared.Return(_signs);
        }
    }
}
