namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>Implemented by the GpuMath flag vector nodes, as <see cref="IDeviceCountedVector"/>.</summary>
internal interface IDeviceCountedBoolVector : IGpuBoolVectorExpression
{
    /// <summary>Evaluates the flag vector elements and their device element count.</summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <returns>The evaluated flag elements and their device element count.</returns>
    BoolVectorEvaluation EvaluateCore(GpuWorkspace workspace);

    /// <inheritdoc />
    GpuBigUIntArrayView IGpuBoolVectorExpression.Evaluate(GpuWorkspace workspace) =>
        DeviceCounted.Materialize(workspace, DeviceCounted.Evaluate(this, workspace));
}
