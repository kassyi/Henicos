namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>A flag vector node evaluated by <see cref="VectorWalk"/>.</summary>
internal interface IBoolVectorOperator : IDeviceCountedBoolVector, IVectorOperator
{
    /// <inheritdoc />
    BoolVectorEvaluation IDeviceCountedBoolVector.EvaluateCore(GpuWorkspace workspace) =>
        VectorWalk.Evaluate(this, workspace, VectorRequest.Whole).Bool;

    // The inputs of a flag node are read whole
    /// <inheritdoc />
    VectorRequest IVectorOperator.OperandRequest(in VectorRequest request) => VectorRequest.Whole;
}
