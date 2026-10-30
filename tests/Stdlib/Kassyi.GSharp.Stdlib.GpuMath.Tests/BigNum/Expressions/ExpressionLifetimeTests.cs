#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// Lifetime of the result buffers of vector expressions (design 4.6): a node caches its result and lends it until the
/// next evaluation or its disposal, grows the cache when a longer evaluation needs it, and never owns or frees a target
/// the caller passes (#630-2, #630-3, #635-1).
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class ExpressionLifetimeTests : IDisposable
{
    private const int Words = 2;

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public ExpressionLifetimeTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void BinaryScalar_WithTarget_WritesTheTarget()
    {
        // With a target the node still needs signs of its own; the values go to the target (E1, #630-2).
        // mutant: the result cache does not provide signs when a target is given (lengths differ, ArgumentException)
        using var array = CreateArray([1, 2]);
        using var scalar = CreateScalar(10);
        using var expression = array.AsVectorizedEnumerable().SelectAdd(scalar);
        using var target = new GpuBigUIntArray(_workspace, 2, expression.WordsPerElement);

        var result = expression.Evaluate(_workspace, targetResult: target);

        Assert.Equal([new BigInteger(11), 12], Read(result));
        Assert.Equal([new BigInteger(11), 12], ReadValues(target));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void BinaryScalar_Dispose_LeavesTheTarget()
    {
        // A target passed after an evaluation into the cache is not the node's: disposing the expression keeps it and
        // returns only the node's cache (E2, #630-2).
        // mutant: the node frees the target in Dispose
        using var array = CreateArray([1, 2]);
        using var scalar = CreateScalar(10);
        var expression = array.AsVectorizedEnumerable().SelectAdd(scalar);
        using var target = new GpuBigUIntArray(_workspace, 2, expression.WordsPerElement);
        var before = _workspace.GetMemoryStatistics();

        expression.Evaluate(_workspace);
        expression.Evaluate(_workspace, targetResult: target);
        expression.Dispose();

        Assert.Equal([new BigInteger(11), 12], ReadValues(target));
        Assert.Equal(before.LiveBuffers, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Unary_Dispose_LeavesTheTarget()
    {
        // The unary node used to adopt the target as its own values and free it on Dispose (E3, #630-3).
        // mutant: the node frees the target in Dispose
        using var array = CreateArray([16, 81]);
        var expression = array.AsVectorizedEnumerable().SelectSqrt();
        using var target = new GpuBigUIntArray(_workspace, 2, expression.WordsPerElement);

        expression.Evaluate(_workspace, targetResult: target);
        expression.Dispose();

        Assert.Equal([new BigInteger(4), 9], ReadValues(target));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LongerEvaluation_GrowsTheCache()
    {
        // A cache sized by a short evaluation is grown for a longer one instead of being overrun (E4, #635-1); the
        // longer evaluation allocates a new buffer, which shows the growth.
        // mutant: the result cache without its capacity check (the second evaluation writes past the first buffer)
        using var array = CreateArray([1, 2, 3, 4, 5]);
        using var scalar = CreateScalar(10);
        using var expression = array.AsVectorizedEnumerable().SelectAdd(scalar);

        expression.Evaluate(_workspace, 0, 2);
        var shortStats = _workspace.GetMemoryStatistics();
        var result = expression.Evaluate(_workspace);
        var longStats = _workspace.GetMemoryStatistics();

        Assert.Equal([new BigInteger(11), 12, 13, 14, 15], Read(result));
        Assert.True(longStats.DriverAllocations > shortStats.DriverAllocations, "the cache was not grown");
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ShorterEvaluation_ReusesTheCache()
    {
        // A cache large enough for the next evaluation is reused (E5).
        // mutant: the result cache reallocates on every evaluation (DriverAllocations grows)
        using var array = CreateArray([1, 2, 3, 4, 5]);
        using var scalar = CreateScalar(10);
        using var expression = array.AsVectorizedEnumerable().SelectAdd(scalar);

        expression.Evaluate(_workspace);
        var fullStats = _workspace.GetMemoryStatistics();
        var result = expression.Evaluate(_workspace, 1, 2);

        Assert.Equal([new BigInteger(12), 13], Read(result));
        Assert.Equal(fullStats.DriverAllocations, _workspace.GetMemoryStatistics().DriverAllocations);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Target_WithWrongShape_IsRejected()
    {
        // A target with another element width would put the kernel's writes at the wrong offsets, and a short one would
        // be overrun (E6).
        // mutant: the target checks removed
        using var array = CreateArray([1, 2]);
        using var scalar = CreateScalar(10);
        using var expression = array.AsVectorizedEnumerable().SelectAdd(scalar);
        using var wide = new GpuBigUIntArray(_workspace, 2, expression.WordsPerElement + 1);
        using var shortTarget = new GpuBigUIntArray(_workspace, 1, expression.WordsPerElement);

        Assert.Throws<ArgumentException>(() => expression.Evaluate(_workspace, targetResult: wide));
        Assert.Throws<ArgumentException>(() => expression.Evaluate(_workspace, targetResult: shortTarget));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Expressions_EvaluatedAndDisposed_ReturnEveryBuffer()
    {
        // Each node returns its cache when the expression is disposed, also after two evaluations (E7).
        // mutant: one node's Dispose without its cache release
        using var left = CreateArray([5, 6, 7]);
        using var right = CreateArray([1, 9, 3]);
        using var scalar = CreateScalar(4);
        var before = _workspace.GetMemoryStatistics();

        using (var chained = left.AsVectorizedEnumerable().SelectAdd(scalar).SelectMultiply(scalar))
        {
            chained.Evaluate(_workspace);
            chained.Evaluate(_workspace);
        }

        using (var compared = left.AsVectorizedEnumerable().ZipAdd(right.AsVectorizedEnumerable())
                   .IsGreaterThan(right.AsVectorizedEnumerable()))
        {
            compared.Evaluate(_workspace);
            compared.Evaluate(_workspace);
        }

        using (var filtered = left.AsVectorizedEnumerable()
                   .Where(left.AsVectorizedEnumerable().IsGreaterThan(scalar)))
        {
            filtered.Evaluate(_workspace);
            filtered.Evaluate(_workspace);
        }

        Assert.Equal(before.LiveBuffers, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Dispose_WithoutEvaluation_AndTwice_IsSafe()
    {
        // A node that never evaluated has nothing to free, and a second Dispose frees nothing again (E8).
        using var array = CreateArray([1, 2]);
        using var scalar = CreateScalar(10);
        var before = _workspace.GetMemoryStatistics();

        var unused = array.AsVectorizedEnumerable().SelectAdd(scalar);
        unused.Dispose();
        var twice = array.AsVectorizedEnumerable().SelectAdd(scalar);
        twice.Evaluate(_workspace);
        twice.Dispose();
        twice.Dispose();

        Assert.Equal(before.LiveBuffers, _workspace.GetMemoryStatistics().LiveBuffers);
        Assert.Equal([new BigInteger(1), 2], Read(array));
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(NodeKind.BinaryScalar)]
    [InlineData(NodeKind.BinaryVector)]
    [InlineData(NodeKind.Unary)]
    public void Target_WithWrongShape_IsRejectedBeforeTheChildren(NodeKind kind)
    {
        // A rejected target leaves the children unevaluated: the caching child below allocates nothing (int side of the
        // float test below; the int nodes always checked first).
        // mutant: the target check moved after the child evaluation (LiveBuffers grows by the child's cache)
        using var array = CreateArray([16, 81]);
        using var scalar = CreateScalar(10);
        using var expression = CreateIntNode(kind, array.AsVectorizedEnumerable().SelectSqrt(), array, scalar);
        using var shortTarget = new GpuBigUIntArray(_workspace, 1, expression.WordsPerElement);
        var before = _workspace.GetMemoryStatistics();

        Assert.Throws<ArgumentException>(() => expression.Evaluate(_workspace, targetResult: shortTarget));

        Assert.Equal(before.LiveBuffers, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void CompoundScalar_ResultIsReturnedEachEvaluation()
    {
        // The scalar operand of a binary-scalar node is a compound expression whose result is a rented buffer; the node
        // returns it after each evaluation (E9, m6).
        // mutant: the node without scalarResult.Dispose() (two rented buffers stay live)
        using var array = CreateArray([1, 2]);
        using var a = CreateScalar(3);
        using var b = CreateScalar(4);
        var before = _workspace.GetMemoryStatistics();

        IGpuBigIntVectorExpression expression = new VectorAddScalarExpression(
            new GpuBigIntVectorLeafExpression(array),
            new SignedAddExpression(new GpuBigIntLeafExpression(a), new GpuBigIntLeafExpression(b)));
        var first = Read(expression.Evaluate(_workspace));
        var second = Read(expression.Evaluate(_workspace));
        expression.Dispose();

        Assert.Equal([new BigInteger(8), 9], first);
        Assert.Equal([new BigInteger(8), 9], second);
        Assert.Equal(before.LiveBuffers, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(NodeKind.BinaryScalar)]
    [InlineData(NodeKind.BinaryVector)]
    [InlineData(NodeKind.Unary)]
    public void Float_WithTarget_WritesTheTarget(NodeKind kind)
    {
        // The float nodes take the values, exponents and signs from the target and return a view of it (E1 for float).
        // mutant: the node writes its cache instead of the target (the target stays zero)
        using var array = CreateFloatArray([4, 9]);
        using var scalar = CreateFloat(10);
        using var expression = CreateFloatNode(kind, array.AsVectorizedEnumerable(), array, scalar);
        using var target = new GpuBigFloatArray(_workspace, 2, expression.WordsPerElement);

        var result = expression.Evaluate(_workspace, targetResult: target);

        Assert.Equal(FloatExpected(kind, [4, 9]), ReadFloat(result));
        Assert.Equal(FloatExpected(kind, [4, 9]), ReadFloat(target));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(NodeKind.BinaryScalar)]
    [InlineData(NodeKind.BinaryVector)]
    [InlineData(NodeKind.Unary)]
    public void Float_LongerEvaluation_GrowsTheCache(NodeKind kind)
    {
        // A float cache sized by a short evaluation is grown for a longer one (E4 for float), and the grown cache is
        // returned on Dispose.
        // mutant: the result cache without its capacity check (the slice past the first buffer throws)
        using var array = CreateFloatArray([4, 9, 16, 25]);
        using var scalar = CreateFloat(10);
        var before = _workspace.GetMemoryStatistics();
        var expression = CreateFloatNode(kind, array.AsVectorizedEnumerable(), array, scalar);

        var shortResult = ReadFloat(expression.Evaluate(_workspace, 0, 2));
        var longResult = ReadFloat(expression.Evaluate(_workspace));
        expression.Dispose();

        Assert.Equal(FloatExpected(kind, [4, 9]), shortResult);
        Assert.Equal(FloatExpected(kind, [4, 9, 16, 25]), longResult);
        Assert.Equal(before.LiveBuffers, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(NodeKind.BinaryScalar)]
    [InlineData(NodeKind.BinaryVector)]
    [InlineData(NodeKind.Unary)]
    public void Float_Target_WithWrongShape_IsRejectedBeforeTheChildren(NodeKind kind)
    {
        // A float target of another width or too short is rejected (E6 for float) before the children run: the caching
        // child below allocates nothing.
        // mutant: the float target length check removed; the check moved after the child evaluation
        using var array = CreateFloatArray([4, 9]);
        using var scalar = CreateFloat(10);
        using var expression = CreateFloatNode(kind, array.AsVectorizedEnumerable().SelectSqrt(), array, scalar);
        using var wide = new GpuBigFloatArray(_workspace, 2, expression.WordsPerElement + 1);
        using var shortTarget = new GpuBigFloatArray(_workspace, 1, expression.WordsPerElement);
        var before = _workspace.GetMemoryStatistics();

        Assert.Throws<ArgumentException>(() => expression.Evaluate(_workspace, targetResult: wide));
        Assert.Throws<ArgumentException>(() => expression.Evaluate(_workspace, targetResult: shortTarget));

        Assert.Equal(before.LiveBuffers, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(NodeKind.BinaryScalar)]
    [InlineData(NodeKind.BinaryVector)]
    [InlineData(NodeKind.Unary)]
    public void Float_Dispose_LeavesTheTarget(NodeKind kind)
    {
        // A float node evaluated into its cache and then into a target returns only its cache on Dispose (E2 for float).
        // mutant: the node frees the target in Dispose, or keeps its cache
        using var array = CreateFloatArray([4, 9]);
        using var scalar = CreateFloat(10);
        using var target = new GpuBigFloatArray(_workspace, 2, CreateFloatWidth(kind, array, scalar));
        var before = _workspace.GetMemoryStatistics();
        var expression = CreateFloatNode(kind, array.AsVectorizedEnumerable(), array, scalar);

        expression.Evaluate(_workspace);
        expression.Evaluate(_workspace, targetResult: target);
        expression.Dispose();

        Assert.Equal(FloatExpected(kind, [4, 9]), ReadFloat(target));
        Assert.Equal(before.LiveBuffers, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0, 3)]
    [InlineData(1, 2)]
    public void Leaf_WithTarget_WritesTheTarget(int skip, int take)
    {
        // A leaf (alone or under Skip / Take) writes the first elements of a target as every other node does, and returns
        // the target's values (Minor 2).
        // mutant: the leaf ignores the target and returns the user's array
        using var array = CreateArray([1, -2, 3]);
        using var expression = array.AsVectorizedEnumerable().Skip(skip).Take(take);
        using var target = new GpuBigUIntArray(_workspace, take + 1, Words);

        var result = expression.Evaluate(_workspace, targetResult: target);

        var expected = new BigInteger[] { 1, -2, 3 }.Skip(skip).Take(take).ToArray();
        Assert.Equal(expected, Read(result));
        Assert.Equal(expected.Select(BigInteger.Abs).Append(BigInteger.Zero), ReadValues(target));
        Assert.Equal(target.DevicePointer, result.Values.DevicePointer);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, 3)]
    [InlineData(1, 2)]
    public void Leaf_Target_WithWrongShape_IsRejected(int skip, int take)
    {
        // A leaf rejects a target of another width or too short, as every other node does (Minor 2).
        // mutant: the leaf without the target check
        using var array = CreateArray([1, 2, 3]);
        using var expression = array.AsVectorizedEnumerable().Skip(skip).Take(take);
        using var wide = new GpuBigUIntArray(_workspace, take, Words + 1);
        using var shortTarget = new GpuBigUIntArray(_workspace, take - 1, Words);

        Assert.Throws<ArgumentException>(() => expression.Evaluate(_workspace, targetResult: wide));
        Assert.Throws<ArgumentException>(() => expression.Evaluate(_workspace, targetResult: shortTarget));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(TargetNode.IntBinaryScalar)]
    [InlineData(TargetNode.IntLeaf)]
    [InlineData(TargetNode.FloatBinaryScalar)]
    [InlineData(TargetNode.FloatLeaf)]
    public void ZeroLengthTarget_IsRejected(TargetNode node)
    {
        // A target of 0 elements has no buffer (Allocate(0) returns the default handle), but it is a target, not "none":
        // a 2-element evaluation into it throws instead of writing the node's cache.
        // mutant: "no target" decided by the handle (IsEmpty) instead of the default view
        using var array = CreateArray([1, 2]);
        using var scalar = CreateScalar(10);
        using var floats = CreateFloatArray([4, 9]);
        using var floatScalar = CreateFloat(10);

        if (node is TargetNode.IntBinaryScalar or TargetNode.IntLeaf)
        {
            using var expression = node == TargetNode.IntLeaf
                ? array.AsVectorizedEnumerable()
                : array.AsVectorizedEnumerable().SelectAdd(scalar);
            using var target = new GpuBigUIntArray(_workspace, 0, expression.WordsPerElement);
            Assert.Throws<ArgumentException>(() => expression.Evaluate(_workspace, targetResult: target));
        }
        else
        {
            using var expression = node == TargetNode.FloatLeaf
                ? floats.AsVectorizedEnumerable()
                : floats.AsVectorizedEnumerable().SelectAdd(floatScalar);
            using var target = new GpuBigFloatArray(_workspace, 0, expression.WordsPerElement);
            Assert.Throws<ArgumentException>(() => expression.Evaluate(_workspace, targetResult: target));
        }
    }

    public enum TargetNode
    {
        IntBinaryScalar,
        IntLeaf,
        FloatBinaryScalar,
        FloatLeaf
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void EmptySlice_Target_WithOtherWidth_IsRejected()
    {
        // A Skip / Take that leaves no element writes nothing, but still rejects a target of another width, as the nodes
        // with a result cache do; the result is empty and borrows nothing (design n8).
        // mutant: the empty Skip / Take without the target check
        using var array = CreateArray([1, 2]);
        using var floats = CreateFloatArray([4, 9]);
        using var skipped = array.AsVectorizedEnumerable().Skip(2);
        using var taken = floats.AsVectorizedEnumerable().Take(0);
        using var wide = new GpuBigUIntArray(_workspace, 1, Words + 1);
        using var wideFloat = new GpuBigFloatArray(_workspace, 1, taken.WordsPerElement + 1);
        var before = _workspace.GetMemoryStatistics();

        Assert.Throws<ArgumentException>(() => skipped.Evaluate(_workspace, targetResult: wide));
        Assert.Throws<ArgumentException>(() => taken.Evaluate(_workspace, targetResult: wideFloat));
        var empty = skipped.Evaluate(_workspace);

        Assert.Equal(0, empty.Length);
        Assert.Equal(Words, empty.WordsPerElement);
        Assert.Equal(before.DriverAllocations, _workspace.GetMemoryStatistics().DriverAllocations);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Leaf_Result_DoesNotOwnTheArray()
    {
        // The leaf lends the user's array: the result owns nothing (Minor 2, design 4.6).
        // mutant: the leaf returns the user's array itself (it owns its buffers)
        using var array = CreateArray([1, 2]);
        using var expression = array.AsVectorizedEnumerable();

        var result = expression.Evaluate(_workspace);

        Assert.False(result.Values.Handle.OwnsBuffer);
        Assert.False(result.Signs.Handle.OwnsBuffer);
        Assert.Equal([new BigInteger(1), 2], Read(result));
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    public void FloatLeaf_WithTarget_WritesTheTarget(int skip, int take)
    {
        // The float leaf (alone or under Skip / Take) writes the first elements of a target and returns them (Minor 2).
        // mutant: the float leaf ignores the target
        using var array = CreateFloatArray([4, 9]);
        using var expression = array.AsVectorizedEnumerable().Skip(skip).Take(take);
        using var target = new GpuBigFloatArray(_workspace, take + 1, expression.WordsPerElement);

        var result = expression.Evaluate(_workspace, targetResult: target);

        var expected = new double[] { 4, 9 }.Skip(skip).Take(take).ToArray();
        Assert.Equal(expected, ReadFloat(result));
        Assert.Equal(expected.Append(0).ToArray(), ReadFloat(target));
        Assert.Equal(target.Significands.DevicePointer, result.Significands.DevicePointer);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void FloatLeaf_Target_WithWrongShape_IsRejected()
    {
        // The float leaf rejects a target of another width or too short (Minor 2).
        // mutant: the float leaf without the target check
        using var array = CreateFloatArray([4, 9]);
        using var expression = array.AsVectorizedEnumerable();
        using var wide = new GpuBigFloatArray(_workspace, 2, expression.WordsPerElement + 1);
        using var shortTarget = new GpuBigFloatArray(_workspace, 1, expression.WordsPerElement);

        Assert.Throws<ArgumentException>(() => expression.Evaluate(_workspace, targetResult: wide));
        Assert.Throws<ArgumentException>(() => expression.Evaluate(_workspace, targetResult: shortTarget));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(BoolKind.CompareScalar)]
    [InlineData(BoolKind.CompareVector)]
    [InlineData(BoolKind.And)]
    [InlineData(BoolKind.Not)]
    [InlineData(BoolKind.FloatCompareScalar)]
    [InlineData(BoolKind.FloatCompareVector)]
    public void BoolResult_IsBorrowed(BoolKind kind)
    {
        // A boolean node lends its result buffer (IGpuBoolVectorExpression XML): the result owns nothing, so that it
        // cannot free the node's buffer, and the node returns the buffer on Dispose.
        // mutant: the node returns its owned result array
        using var array = CreateArray([1, 20]);
        using var scalar = CreateScalar(10);
        using var floats = CreateFloatArray([4, 9]);
        using var floatScalar = CreateFloat(5);
        var before = _workspace.GetMemoryStatistics();
        var expression = CreateBoolNode(kind, array, scalar, floats, floatScalar);

        var result = expression.Evaluate(_workspace);
        var owns = result.Handle.OwnsBuffer;
        var mask = new uint[2];
        _workspace.Provider.CopyDeviceToHost(mask, result.DevicePointer, 2, _workspace.Stream);
        expression.Dispose();

        Assert.False(owns);
        Assert.Equal(ExpectedMask(kind), mask);
        Assert.Equal(before.LiveBuffers, _workspace.GetMemoryStatistics().LiveBuffers);
    }

    public enum BoolKind
    {
        CompareScalar,
        CompareVector,
        And,
        Not,
        FloatCompareScalar,
        FloatCompareVector
    }

    private static IGpuBoolVectorExpression CreateBoolNode(
        BoolKind kind, GpuBigIntArray array, GpuBigInt scalar, GpuBigFloatArray floats, GpuBigFloat floatScalar) =>
        kind switch
        {
            // [1, 20] > 10 -> [0, 1]
            BoolKind.CompareScalar => array.AsVectorizedEnumerable().IsGreaterThan(scalar),
            // [1, 20] == [1, 20] -> [1, 1]
            BoolKind.CompareVector => array.AsVectorizedEnumerable().IsEqualTo(array.AsVectorizedEnumerable()),
            // ([1, 20] > 10) & ([1, 20] == [1, 20]) -> [0, 1]
            BoolKind.And => array.AsVectorizedEnumerable().IsGreaterThan(scalar)
                .And(array.AsVectorizedEnumerable().IsEqualTo(array.AsVectorizedEnumerable())),
            // !([1, 20] > 10) -> [1, 0]
            BoolKind.Not => array.AsVectorizedEnumerable().IsGreaterThan(scalar).Not(),
            // [4, 9] > 5 -> [0, 1]
            BoolKind.FloatCompareScalar => floats.AsVectorizedEnumerable().IsGreaterThan(floatScalar),
            // [4, 9] < [4, 9] -> [0, 0]
            BoolKind.FloatCompareVector => floats.AsVectorizedEnumerable().IsLessThan(floats.AsVectorizedEnumerable()),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static uint[] ExpectedMask(BoolKind kind) =>
        kind switch
        {
            BoolKind.CompareVector => [1, 1],
            BoolKind.Not => [1, 0],
            BoolKind.FloatCompareVector => [0, 0],
            _ => [0, 1]
        };

    public enum NodeKind
    {
        BinaryScalar,
        BinaryVector,
        Unary
    }

    private static IGpuBigIntVectorExpression CreateIntNode(
        NodeKind kind, IGpuBigIntVectorExpression child, GpuBigIntArray other, GpuBigInt scalar) =>
        kind switch
        {
            NodeKind.BinaryScalar => child.SelectAdd(scalar),
            NodeKind.BinaryVector => child.ZipAdd(other.AsVectorizedEnumerable()),
            NodeKind.Unary => child.SelectSqrt(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static IGpuBigFloatVectorExpression CreateFloatNode(
        NodeKind kind, IGpuBigFloatVectorExpression child, GpuBigFloatArray other, GpuBigFloat scalar) =>
        kind switch
        {
            NodeKind.BinaryScalar => child.SelectAdd(scalar),
            NodeKind.BinaryVector => child.ZipAdd(other.AsVectorizedEnumerable()),
            NodeKind.Unary => child.SelectSqrt(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static int CreateFloatWidth(NodeKind kind, GpuBigFloatArray array, GpuBigFloat scalar)
    {
        using var probe = CreateFloatNode(kind, array.AsVectorizedEnumerable(), array, scalar);
        return probe.WordsPerElement;
    }

    private static double[] FloatExpected(NodeKind kind, double[] inputs) =>
        inputs.Select(x => kind switch
        {
            NodeKind.BinaryScalar => x + 10,
            NodeKind.BinaryVector => x + x,
            _ => Math.Sqrt(x)
        }).ToArray();

    private GpuBigFloat CreateFloat(uint value)
    {
        var words = new uint[Words * 2];
        words[0] = value;
        return new GpuBigFloat(new GpuBigUInt(_workspace, words), 0, false);
    }

    private GpuBigFloatArray CreateFloatArray(uint[] values)
    {
        var array = new GpuBigFloatArray(_workspace, values.Length, Words * 2);
        for (var i = 0; i < values.Length; i++)
        {
            using var element = CreateFloat(values[i]);
            array.SetElement(i, element);
        }

        return array;
    }

    private static double[] ReadFloat(GpuBigFloatArrayView array)
    {
        var result = new double[array.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var element = array.GetElement(i);
            var magnitude = (double)element.Significand.ToBigInteger() * Math.Pow(2.0, element.Exponent);
            // Rounded to 8 digits so that a square root compares exactly with Math.Sqrt
            result[i] = Math.Round(element.IsNegative ? -magnitude : magnitude, 8);
        }

        return result;
    }

    private GpuBigIntArray CreateArray(BigInteger[] values)
    {
        var array = new GpuBigIntArray(_workspace, values.Length, Words);
        var words = new uint[values.Length * Words];
        var signs = new uint[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var magnitude = BigInteger.Abs(values[i]);
            words[i * Words] = (uint)(magnitude & uint.MaxValue);
            words[i * Words + 1] = (uint)(magnitude >> 32);
            signs[i] = values[i].Sign < 0 ? 1u : 0u;
        }

        _workspace.Provider.CopyHostToDevice(array.Values.DevicePointer, words, words.Length, _workspace.Stream);
        _workspace.Provider.CopyHostToDevice(array.Signs.DevicePointer, signs, signs.Length, _workspace.Stream);
        return array;
    }

    private GpuBigInt CreateScalar(BigInteger value) =>
        new(GpuBigUInt.FromBigInteger(_workspace, BigInteger.Abs(value), Words), value.Sign < 0);

    private BigInteger[] Read(GpuBigIntArrayView array)
    {
        var values = ReadValues(array.Values);
        var signs = new uint[array.Length];
        _workspace.Provider.CopyDeviceToHost(signs, array.Signs.DevicePointer, signs.Length, _workspace.Stream);
        return values.Select((v, i) => signs[i] != 0 ? -v : v).ToArray();
    }

    private BigInteger[] ReadValues(GpuBigUIntArrayView values)
    {
        var host = new uint[values.TotalWords];
        _workspace.Provider.CopyDeviceToHost(host, values.DevicePointer, host.Length, _workspace.Stream);
        var result = new BigInteger[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var value = BigInteger.Zero;
            for (var w = values.WordsPerElement - 1; w >= 0; w--)
            {
                value = (value << 32) | host[i * values.WordsPerElement + w];
            }

            result[i] = value;
        }

        return result;
    }
}
