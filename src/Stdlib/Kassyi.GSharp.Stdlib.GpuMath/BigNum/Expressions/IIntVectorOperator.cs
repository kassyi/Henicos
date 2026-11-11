namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>An integer vector node evaluated by <see cref="VectorWalk"/>.</summary>
internal interface IIntVectorOperator : IDeviceCountedVector, IVectorOperator
{
    /// <inheritdoc />
    VectorEvaluation IDeviceCountedVector.EvaluateCore(GpuWorkspace workspace, int elementOffset, int? elementCount,
        GpuBigUIntArrayView targetResult) =>
        VectorWalk.Evaluate(this, workspace,
            new VectorRequest(elementOffset, elementCount, VectorStorage.Of(targetResult))).Int;
}
