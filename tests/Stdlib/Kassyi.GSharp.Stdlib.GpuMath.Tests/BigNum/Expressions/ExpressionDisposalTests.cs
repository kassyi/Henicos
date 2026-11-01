#nullable enable
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// Disposal of <see cref="GpuBigIntExpr"/> and vector expressions across value-type copies, shared sub-expressions,
/// compound signed operators, and CUDA graphs.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class ExpressionDisposalTests : IDisposable
{
    private const int Words = 2;
    private const int ElementCount = 8;

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;
    private readonly GpuBigIntArray _array1;
    private readonly GpuBigIntArray _array2;
    private readonly GpuBigInt _scalar;
    private readonly GpuBigInt _zero;

    public ExpressionDisposalTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        _array1 = CreateIntArray([10, -3, 7, 12, -5, 4, 8, -1]);
        _array2 = CreateIntArray([2, 6, -4, 9, 1, -8, 3, 5]);
        _scalar = new GpuBigInt(GpuBigUInt.FromBigInteger(_workspace, 3, Words), false);
        _zero = new GpuBigInt(GpuBigUInt.FromBigInteger(_workspace, 0, Words), false);
    }

    public void Dispose()
    {
        _zero.Dispose();
        _scalar.Dispose();
        _array2.Dispose();
        _array1.Dispose();
        _workspace.Dispose();
        _scope.Dispose();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void RepeatedLoopWithUsing_LeavesLiveBuffersUnchanged()
    {
        // mutant: GpuBigIntExpr.Dispose is a no-op (each iteration leaves the SelectAdd cache alive)
        var initialLive = _workspace.GetMemoryStatistics().LiveBuffers;

        for (var i = 0; i < 10; i++)
        {
            using var expr = _array1.AsVectorizedEnumerable().SelectAdd(_scalar).Sum();
            using var result = expr.Evaluate(_workspace);
            Assert.Equal(new BigInteger(56), result.Value.ToBigInteger(result.IsNegative));
        }

        Assert.Equal(initialLive, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ValueTypeCopiesAndDefault_DisposeIdempotently()
    {
        // mutant: VectorResultCache.Release does not clear the field on first Dispose (second copy double-frees)
        // mutant: default(GpuBigIntExpr).Dispose throws NullReferenceException
        default(GpuBigIntExpr).Dispose();

        var initialLive = _workspace.GetMemoryStatistics().LiveBuffers;
        var expr = _array1.AsVectorizedEnumerable().SelectAdd(_scalar).Sum();
        var copy = expr;

        using (var result = expr.Evaluate(_workspace))
        {
            Assert.Equal(new BigInteger(56), result.Value.ToBigInteger(result.IsNegative));
            Assert.True(_workspace.GetMemoryStatistics().LiveBuffers > initialLive);
        }

        expr.Dispose();
        copy.Dispose();
        expr.Dispose();

        Assert.Equal(initialLive, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DiamondSharedSubexpression_DisposesOnceWithoutError()
    {
        // mutant: shared sub-expression is not idempotent when reached down both Left and Right of SignedAddExpression
        var initialLive = _workspace.GetMemoryStatistics().LiveBuffers;

        var sum = _array1.AsVectorizedEnumerable().SelectAdd(_scalar).Sum();
        using (var doubled = sum + sum)
        {
            using var result = doubled.Evaluate(_workspace);
            Assert.Equal(new BigInteger(112), result.Value.ToBigInteger(result.IsNegative));
        }

        sum.Dispose();
        Assert.Equal(initialLive, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LifoUsingDeclarations_DisposeChildAndParentWithoutError()
    {
        // C# disposes nested using declarations in LIFO order: sum (outer parent node) first, then vec (inner child node).
        var initialLive = _workspace.GetMemoryStatistics().LiveBuffers;

        using (var vec = _array1.AsVectorizedEnumerable().SelectAdd(_scalar))
        using (var sum = vec.Sum())
        {
            using var result = sum.Evaluate(_workspace);
            Assert.Equal(new BigInteger(56), result.Value.ToBigInteger(result.IsNegative));
        }

        Assert.Equal(initialLive, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CompoundSignedExpressions_PropagateDisposeThroughUnaryAndBinaryNodes()
    {
        // mutant: SignedBinaryExpression, SignedNegateExpression or SignedUnaryExpression does not forward Dispose to children
        var initialLive = _workspace.GetMemoryStatistics().LiveBuffers;

        using (var compound = -(_array1.AsVectorizedEnumerable().SelectAdd(_scalar).Sum()) *
                              (~(_array2.AsVectorizedEnumerable().Where(_array2.AsVectorizedEnumerable().IsGreaterThan(_zero)).Count())))
        {
            using var result = compound.Evaluate(_workspace);
            Assert.NotEqual(BigInteger.Zero, result.Value.ToBigInteger(result.IsNegative));
            Assert.True(_workspace.GetMemoryStatistics().LiveBuffers > initialLive);
        }

        Assert.Equal(initialLive, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CompiledGraphDisposal_ReleasesOwnedCompoundExpressionCaches()
    {
        // GraphHandles.Destroy calls (_node as IDisposable)?.Dispose() when _ownsExpression is true.
        // With SignedBinaryExpression, SignedNegateExpression and SignedUnaryExpression implementing IDisposable,
        // disposing the graph alone frees all cached vector buffers in the compound expression tree.
        var initialLive = _workspace.GetMemoryStatistics().LiveBuffers;

        var leftVec = _array1.AsVectorizedEnumerable().SelectAdd(_scalar);
        var rightVec = _array2.AsVectorizedEnumerable().SelectAdd(_scalar);
        var compound = -(leftVec.Where(leftVec.IsGreaterThan(_zero)).Count()) +
                       (-(~(rightVec.Where(rightVec.IsGreaterThan(_zero)).Count())));

        using (var graph = compound.CompileToGraph(_workspace))
        {
            graph.Execute();
            Assert.True(_workspace.GetMemoryStatistics().LiveBuffers > initialLive);
        }

        Assert.Equal(initialLive, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void DisposingExpressionOwnedByLiveGraph_ThrowsAndLeavesGraphRunnable()
    {
        // mutant: GpuBigIntExpr.Dispose skips GraphOwnership.ThrowIfOwned(Node)
        var initialLive = _workspace.GetMemoryStatistics().LiveBuffers;

        var vec = _array1.AsVectorizedEnumerable().SelectAdd(_scalar);
        var expr = -(vec.Where(vec.IsGreaterThan(_zero)).Count());

        var graph = expr.CompileToGraph(_workspace);
        try
        {
            Assert.Throws<InvalidOperationException>(() => expr.Dispose());

            graph.Execute();
            Assert.Equal(new BigInteger(-6), graph.Result.Value.ToBigInteger(graph.Result.IsNegative));
        }
        finally
        {
            graph.Dispose();
        }

        // After the graph is disposed, ownership is released and repeated Dispose calls are safe no-ops.
        expr.Dispose();
        Assert.Equal(initialLive, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void FloatVectorExpression_RepeatedLoopAndRepeatedDispose_LeavesLiveBuffersUnchanged()
    {
        using var floatArray = new GpuBigFloatArray(_workspace, ElementCount, Words);
        using var floatScalarSig = GpuBigUInt.FromBigInteger(_workspace, 2, Words);
        var floatScalar = new GpuBigFloat(floatScalarSig, 0, false);

        var initialLive = _workspace.GetMemoryStatistics().LiveBuffers;

        for (var i = 0; i < 10; i++)
        {
            using var floatVec = floatArray.AsVectorizedEnumerable().SelectAdd(floatScalar);
            using var sum = floatVec.Sum(_workspace);
            floatVec.Dispose();
        }

        Assert.Equal(initialLive, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    private GpuBigIntArray CreateIntArray(int[] values)
    {
        var array = new GpuBigIntArray(_workspace, values.Length, Words);
        var words = new uint[values.Length * Words];
        var signs = new uint[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            words[i * Words] = (uint)Math.Abs(values[i]);
            signs[i] = values[i] < 0 ? 1u : 0u;
        }

        _workspace.Provider.CopyHostToDevice(array.Values.DevicePointer, words, words.Length, _workspace.Stream);
        _workspace.Provider.CopyHostToDevice(array.Signs.DevicePointer, signs, signs.Length, _workspace.Stream);
        return array;
    }
}
