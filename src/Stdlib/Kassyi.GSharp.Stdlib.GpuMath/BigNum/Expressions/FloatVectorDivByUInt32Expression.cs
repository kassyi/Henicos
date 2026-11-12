namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;
using Providers;

internal sealed class FloatVectorDivByUInt32Expression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    uint divisor) : FloatVectorUnaryExpressionBase(vector)
{
    protected override bool ElementMayThrow => divisor == 0;

    // The divisor as a one-word float (exponent 0) while the elements are evaluated; default otherwise
    private GpuFloatOperand _divisorOperand;

    protected override void ExecuteElements(
        GpuWorkspace workspace,
        GpuBigFloatArrayView source,
        GpuBigFloatArrayView destination,
        int count)
    {
        if (count == 0)
        {
            return;
        }

        // C# divides only the elements, so a zero divisor over none divides nothing, as System.Linq
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }

        using var divisorWord = workspace.RentTemp(1);
        workspace.Provider.WriteWord(divisorWord.DevicePointer, 0, divisor, workspace.Stream);
        _divisorOperand = new GpuFloatOperand(divisorWord.Handle, 0);
        try
        {
            base.ExecuteElements(workspace, source, destination, count);
        }
        finally
        {
            _divisorOperand = default;
        }
    }

    // The quotient rounded toward zero to the precision of the element and normalized, as every float division
    protected override GpuFloatResult EvaluateElement(
        ref GpuBigIntExecutionContext context,
        IGpuBigFloatArithmetic arithmetic,
        in GpuFloatOperand source,
        GpuBigIntMemoryHandle destination) =>
        arithmetic.Divide(ref context, in source, in _divisorOperand, destination);
}
