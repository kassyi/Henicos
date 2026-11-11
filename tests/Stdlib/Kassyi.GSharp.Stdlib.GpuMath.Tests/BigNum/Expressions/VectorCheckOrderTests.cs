#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of the length checks of the vector nodes, recorded with leaves that log when they are evaluated:
// - a node whose inputs have different lengths throws ArgumentException before any input is evaluated (the float
//   comparison, the float Zips and the five integer Zips, whose checks run before their inputs are evaluated); inputs
//   of the same length are evaluated left, then right.
// - a Zip below another node rejects its own inputs: the exception is the Zip's ArgumentException, not an
//   ArgumentOutOfRangeException from a leaf sliced past its end, and no leaf is evaluated.

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
    // without VectorShape.ThrowIfLengthsDiffer (the division runs over the shorter input)
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

    public static TheoryData<string, int, int> IntZipLengths
    {
        get
        {
            var data = new TheoryData<string, int, int>();
            foreach (var op in s_intZips)
            {
                data.Add(op, 4, 6);
                data.Add(op, 6, 4);
            }

            return data;
        }
    }

    // Every integer Zip rejects inputs of different lengths, in both orders: a shorter left side no longer computes
    // over the first elements of the right side, and a longer one no longer slices the right leaf past its end.
    // System.Linq's Zip would truncate to the shorter input; GpuMath throws, as its float Zips do.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/VectorBinaryVectorExpressions.cs Enter ->
    // without VectorShape.ThrowIfLengthsDiffer (ZipAdd 4/6: 4 elements, no exception; ZipAdd 6/4:
    // ArgumentOutOfRangeException from the right leaf)
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(IntZipLengths))]
    public void IntZip_DifferentLengths_ThrowsBeforeEvaluatingAnInput(string op, int leftLength, int rightLength)
    {
        using var left = IntArray(Ones(leftLength));
        using var right = IntArray(Ones(rightLength));
        using var zipped = Zip(op, Leaf("left", left), Leaf("right", right));

        Assert.Throws<ArgumentException>(() => zipped.Evaluate(_workspace));
        Assert.Empty(_log);
    }

    public static TheoryData<string, string[]> NestedMismatches => new()
    {
        { "a6.ZipAdd(a4.ZipAdd(b4))", [] },
        { "a6.ZipDivide(b6.ZipAdd(b4))", ["a6"] },
        { "a6.ZipAdd(b6).ZipAdd(a4.ZipSubtract(b4))", [] },
        { "a6.ZipMultiply(b6.ZipModulo(b4))", ["a6"] },
        { "a6.ZipAdd(b6.Take(4))", [] },
    };

    // A mismatch at or one level below a Zip: the Zip whose inputs differ rejects them with its ArgumentException
    // before either of them is evaluated (Assert.Throws is exact, so the ArgumentOutOfRangeException of a sliced leaf
    // fails it). Under an outer Zip whose own lengths match (6 = 6), the outer left input is evaluated first, in the C#
    // order of the operands; nothing below the mismatched Zip is.
    // mutant: the same Enter without ThrowIfLengthsDiffer (rows 1 to 4: ArgumentOutOfRangeException `elementOffset`;
    // row 5: the check after evaluation throws, with both leaves logged)
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(NestedMismatches))]
    public void IntZip_MismatchBelowAnotherZip_ThrowsBeforeEvaluatingItsInputs(string shape, string[] evaluated)
    {
        using var a6 = IntArray(Ones(6));
        using var b6 = IntArray(Ones(6));
        using var a4 = IntArray(Ones(4));
        using var b4 = IntArray(Ones(4));
        using var expr = shape switch
        {
            "a6.ZipAdd(a4.ZipAdd(b4))" => Leaf("a6", a6).ZipAdd(Leaf("a4", a4).ZipAdd(Leaf("b4", b4))),
            "a6.ZipDivide(b6.ZipAdd(b4))" => Leaf("a6", a6).ZipDivide(Leaf("b6", b6).ZipAdd(Leaf("b4", b4))),
            "a6.ZipAdd(b6).ZipAdd(a4.ZipSubtract(b4))" => Leaf("a6", a6).ZipAdd(Leaf("b6", b6))
                .ZipAdd(Leaf("a4", a4).ZipSubtract(Leaf("b4", b4))),
            "a6.ZipMultiply(b6.ZipModulo(b4))" =>
                Leaf("a6", a6).ZipMultiply(Leaf("b6", b6).ZipModulo(Leaf("b4", b4))),
            "a6.ZipAdd(b6.Take(4))" => Leaf("a6", a6).ZipAdd(Leaf("b6", b6).Take(4)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
        };

        var error = Assert.Throws<ArgumentException>(() => expr.Evaluate(_workspace));
        Assert.Equal("Vector lengths must match for Zip operations.", error.Message);
        Assert.Equal(evaluated, _log);
    }

    // Inputs of the same length are evaluated left, then right, and every element is combined (the check rejects
    // nothing it should accept).
    // mutant: VectorShape.ThrowIfLengthsDiffer `leftCapacity != rightCapacity` -> `leftCapacity == rightCapacity`
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("ZipAdd", new uint[] { 9, 12, 17 })]
    [InlineData("ZipSubtract", new uint[] { 5, 6, 7 })]
    [InlineData("ZipMultiply", new uint[] { 14, 27, 60 })]
    [InlineData("ZipDivide", new uint[] { 3, 3, 2 })]
    [InlineData("ZipModulo", new uint[] { 1, 0, 2 })]
    public void IntZip_SameLengths_EvaluatesLeftThenRight(string op, uint[] expected)
    {
        uint[] left = [7, 9, 12];
        uint[] right = [2, 3, 5];
        using var leftArray = IntArray(left);
        using var rightArray = IntArray(right);
        using var zipped = Zip(op, Leaf("left", leftArray), Leaf("right", rightArray));

        var values = ReadInts(zipped.Evaluate(_workspace));

        Assert.Equal(["left", "right"], _log);
        // The expected row is the operation of each pair, as System.Linq's Zip computes it over the same values.
        Assert.Equal(left.Zip(right, (l, r) => Apply(op, l, r)).ToArray(), expected);
        Assert.Equal(expected, values);
    }

    private static readonly string[] s_intZips = ["ZipAdd", "ZipSubtract", "ZipMultiply", "ZipDivide", "ZipModulo"];

    private static uint Apply(string op, uint left, uint right) => op switch
    {
        "ZipAdd" => left + right,
        "ZipSubtract" => left - right,
        "ZipMultiply" => left * right,
        "ZipDivide" => left / right,
        "ZipModulo" => left % right,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
    };

    private static IGpuBigIntVectorExpression Zip(string op, IGpuBigIntVectorExpression left,
        IGpuBigIntVectorExpression right) => op switch
    {
        "ZipAdd" => left.ZipAdd(right),
        "ZipSubtract" => left.ZipSubtract(right),
        "ZipMultiply" => left.ZipMultiply(right),
        "ZipDivide" => left.ZipDivide(right),
        "ZipModulo" => left.ZipModulo(right),
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
    };

    private static uint[] Ones(int length) => Enumerable.Repeat(1u, length).ToArray();

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
