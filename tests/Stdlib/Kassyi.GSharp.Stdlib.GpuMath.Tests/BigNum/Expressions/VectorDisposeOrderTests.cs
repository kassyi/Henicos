#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Dispose releases every node; one exception is rethrown as it is, several (the root's own and an input's included)
// are thrown together in an AggregateException, in the order the nodes were released.

/// <summary>Dispose of the vector nodes; nothing is evaluated, so no GPU is needed.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class VectorDisposeOrderTests
{
    private readonly List<string> _log = [];

    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/ExpressionDisposal.cs Dispose -> the
    // exceptions not collected (the release of the root ends the walk)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void RootAndInputBothThrow_ThrowsBothInReleaseOrder()
    {
        var root = new ThrowingOperator(new DisposableLeaf("input", _log, throws: true), _log);

        var thrown = Assert.Throws<AggregateException>(root.Dispose);

        Assert.Equal(["root released", "input disposed"], _log);
        Assert.Equal(["root", "input"], thrown.InnerExceptions.Select(static e => e.Message));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ZipOfTwoThrowingInputs_DisposesBothAndThrowsBoth()
    {
        var zip = new DisposableLeaf("left", _log, throws: true)
            .ZipAdd(new DisposableLeaf("right", _log, throws: true));

        var thrown = Assert.Throws<AggregateException>(zip.Dispose);

        Assert.Equal(["left disposed", "right disposed"], _log);
        Assert.Equal(["left", "right"], thrown.InnerExceptions.Select(static e => e.Message));
    }

    // mutant: ExpressionDisposal.cs Dispose -> an AggregateException also for a single exception
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void OneThrowingInput_RethrowsItsExceptionAsItIs()
    {
        var comparison = new DisposableLeaf("left", _log, throws: true)
            .IsLessThan(new DisposableLeaf("right", _log, throws: false));

        var thrown = Assert.Throws<InvalidOperationException>(comparison.Dispose);

        Assert.Equal("left", thrown.Message);
        Assert.Equal(["left disposed", "right disposed"], _log);
    }

    // A leaf of one 1-word element that logs when it is disposed, and may throw then; it is never evaluated
    private sealed class DisposableLeaf(string name, List<string> log, bool throws) : IGpuBigIntVectorExpression
    {
        public int Length => 1;

        public int WordsPerElement => 1;

        public GpuBigIntArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
            GpuBigUIntArrayView targetResult = default) =>
            throw new NotSupportedException("The leaf is not evaluated.");

        public void Dispose()
        {
            log.Add(name + " disposed");
            if (throws)
            {
                throw new InvalidOperationException(name);
            }
        }
    }

    // A vector node whose own release throws, over one input
    private sealed class ThrowingOperator(IGpuBigIntVectorExpression input, List<string> log)
        : IGpuBigIntVectorExpression, IIntVectorOperator
    {
        public int Length => input.Length;

        public int WordsPerElement => input.WordsPerElement;

        public int OperandCount => 1;

        public object GetOperand(int index) => input;

        public VectorRequest OperandRequest(in VectorRequest request) => request;

        public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
            VectorResult second) =>
            throw new NotSupportedException("The node is not evaluated.");

        public VectorResult EvaluateDirect(GpuWorkspace workspace, scoped in VectorRequest request, int depth) =>
            throw new NotSupportedException("The node is not evaluated.");

        public void DisposeOwn()
        {
            log.Add("root released");
            throw new InvalidOperationException("root");
        }

        public void Dispose() => VectorWalk.Dispose(this);
    }
}
