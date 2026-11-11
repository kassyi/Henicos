namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>A floating-point vector node evaluated by <see cref="VectorWalk"/>.</summary>
internal interface IFloatVectorOperator : IDeviceCountedFloatVector, IVectorOperator
{
    /// <inheritdoc />
    FloatVectorEvaluation IDeviceCountedFloatVector.EvaluateCore(GpuWorkspace workspace, int elementOffset,
        int? elementCount, GpuBigFloatArrayView targetResult) =>
        VectorWalk.Evaluate(this, workspace,
            new VectorRequest(elementOffset, elementCount, VectorStorage.Of(targetResult))).Float;
}
