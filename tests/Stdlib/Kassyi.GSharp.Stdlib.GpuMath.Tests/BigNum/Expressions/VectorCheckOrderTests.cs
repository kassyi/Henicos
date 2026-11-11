#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of the length checks of the vector nodes, recorded with leaves that log when they are evaluated:
// - a node whose inputs have different lengths throws ArgumentException before any input is evaluated (the float
//   comparison, the float Zip and the integer ZipDivide, whose checks run before their inputs are evaluated); inputs
//   of the same length are evaluated left, then right.

/// <summary>The length checks of the vector nodes, on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class VectorCheckOrderTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;
    private readonly List<string> _log = [];

    public VectorCheckOrderTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/FloatVectorCompareExpressions.cs
    // FloatVectorCompareVectorExpression.Enter -> no check (the kernel reads the shorter input past its end)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void FloatCompareVector_DifferentLengths_ThrowsBeforeEvaluatingAnInput()
    {
        using var shorter = FloatArray([1, 2]);
        using var longer = FloatArray([1, 2, 3]);
        using var comparison = Leaf("left", shorter).IsLessThan(Leaf("right", longer));

        Assert.Throws<ArgumentException>(() => comparison.Evaluate(_workspace));
        Assert.Empty(_log);
    }

    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/FloatVectorBinaryVectorExpressions.cs
    // EvaluateDirect -> without `Enter(request)` (the inputs are evaluated before the lengths are compared)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void FloatZip_DifferentLengths_ThrowsBeforeEvaluatingAnInput()
    {
        using var shorter = FloatArray([1, 2]);
        using var longer = FloatArray([1, 2, 3]);
        using var difference = Leaf("left", longer).ZipSubtract(Leaf("right", shorter));

        Assert.Throws<ArgumentException>(() => difference.Evaluate(_workspace));
        Assert.Empty(_log);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void FloatCompareVector_SameLengths_EvaluatesLeftThenRight()
    {
        using var left = FloatArray([1, 5, 3]);
        using var right = FloatArray([2, 4, 3]);
        using var comparison = Leaf("left", left).IsLessThan(Leaf("right", right));

        var flags = ReadFlags(comparison.Evaluate(_workspace));

        Assert.Equal(["left", "right"], _log);
        Assert.Equal([1u, 0u, 0u], flags);
    }

    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/VectorBinaryVectorExpressions.cs Enter ->
    // without the check of checkConstructorLength (the division runs over the shorter input)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ZipDivide_DifferentLengths_ThrowsBeforeEvaluatingAnInput()
    {
        using var shorter = IntArray([1, 2]);
        using var longer = IntArray([1, 2, 3]);
        using var quotient = Leaf("left", longer).ZipDivide(Leaf("right", shorter));

        Assert.Throws<ArgumentException>(() => quotient.Evaluate(_workspace));
        Assert.Empty(_log);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ZipDivide_SameLengths_EvaluatesLeftThenRight()
    {
        using var dividends = IntArray([7, 9, 12]);
        using var divisors = IntArray([2, 3, 5]);
        using var quotient = Leaf("left", dividends).ZipDivide(Leaf("right", divisors));

        var values = ReadInts(quotient.Evaluate(_workspace));

        Assert.Equal(["left", "right"], _log);
        Assert.Equal([3u, 3u, 2u], values);
    }

    private IGpuBigIntVectorExpression Leaf(string name, GpuBigIntArray array) =>
        new LoggingIntLeaf(name, array.AsVectorizedEnumerable(), _log);

    private IGpuBigFloatVectorExpression Leaf(string name, GpuBigFloatArray array) =>
        new LoggingFloatLeaf(name, array.AsVectorizedEnumerable(), _log);

    private GpuBigIntArray IntArray(uint[] values)
    {
        var array = new GpuBigIntArray(_workspace, values.Length, 1);
        _workspace.Provider.CopyHostToDevice(array.Values.DevicePointer, values, values.Length);
        _workspace.Provider.CopyHostToDevice(array.Signs.DevicePointer, new uint[values.Length], values.Length);
        return array;
    }

    private GpuBigFloatArray FloatArray(long[] values)
    {
        var array = new GpuBigFloatArray(_workspace, values.Length, 4);
        for (var i = 0; i < values.Length; i++)
        {
            using var element = GpuBigFloatTestHelpers.CreateFloat(_workspace, values[i]);
            array.SetElement(i, element);
        }

        return array;
    }

    // The low word of each element
    private uint[] ReadInts(GpuBigIntArrayView view)
    {
        var words = new uint[view.Values.TotalWords];
        _workspace.Provider.CopyDeviceToHost(words, view.Values.DevicePointer, words.Length);
        var stride = view.WordsPerElement;
        return Enumerable.Range(0, view.Length).Select(i => words[i * stride]).ToArray();
    }

    private uint[] ReadFlags(GpuBigUIntArrayView view)
    {
        var flags = new uint[view.Length];
        _workspace.Provider.CopyDeviceToHost(flags, view.DevicePointer, flags.Length);
        return flags;
    }

    // An integer leaf that logs its name when it is evaluated
    private sealed class LoggingIntLeaf(string name, IGpuBigIntVectorExpression inner, List<string> log)
        : IGpuBigIntVectorExpression
    {
        public int Length => inner.Length;

        public int WordsPerElement => inner.WordsPerElement;

        public GpuBigIntArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
            GpuBigUIntArrayView targetResult = default)
        {
            log.Add(name);
            return inner.Evaluate(workspace, elementOffset, elementCount, targetResult);
        }

        public void Dispose() => inner.Dispose();
    }

    // The same for a floating-point leaf
    private sealed class LoggingFloatLeaf(string name, IGpuBigFloatVectorExpression inner, List<string> log)
        : IGpuBigFloatVectorExpression
    {
        public int Length => inner.Length;

        public int WordsPerElement => inner.WordsPerElement;

        public GpuBigFloatArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
            GpuBigFloatArrayView targetResult = default)
        {
            log.Add(name);
            return inner.Evaluate(workspace, elementOffset, elementCount, targetResult);
        }

        public void Dispose() => inner.Dispose();
    }
}
