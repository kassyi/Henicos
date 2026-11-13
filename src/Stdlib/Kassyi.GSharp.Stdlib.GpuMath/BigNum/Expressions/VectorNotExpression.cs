namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorNotExpression([TakesOwnership] IGpuBoolVectorExpression source)
    : IGpuBoolVectorExpression, IBoolVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(source);

    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(source);

    public bool MayThrow => _inputsMayThrow;

    // The flags, allocated at the first evaluation
    private readonly VectorFlags _flags = new();

    public int Length => _shape.Length;
    public int Capacity => _shape.Capacity;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 1;

    public object GetOperand(int index) => source;

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second) =>
        VectorResult.Of(Apply(workspace, first.Bool));

    private BoolVectorEvaluation Apply(GpuWorkspace workspace, BoolVectorEvaluation result)
    {
        var flags = _flags.Get(workspace, Capacity);

        // Not of empty vectors launches nothing: a grid of 0 blocks is an invalid launch.
        if (Capacity > 0)
        {
            GpuBigIntCmpKernels.LaunchVectorNotKernelPtr(result.Flags.ToProxy(), flags.ToProxy(), Capacity,
                (uint)((Capacity + 255) / 256), 1, 1, 256, 1, 1, 0, workspace.Stream);
        }

        return new BoolVectorEvaluation(flags, result.Count);
    }

    // One level of a recursive evaluation (VectorWalk.EvaluateDirect)
    public VectorResult EvaluateDirect(GpuWorkspace workspace, scoped in VectorRequest request,
        RecursionLevel level)
    {
        return Apply(workspace, request, VectorWalk.EvaluateDirect(source, workspace, VectorRequest.Whole, level),
            default);
    }

    public void DisposeOwn() => _flags.Dispose();

    public void Dispose() => VectorWalk.Dispose(this);
}
