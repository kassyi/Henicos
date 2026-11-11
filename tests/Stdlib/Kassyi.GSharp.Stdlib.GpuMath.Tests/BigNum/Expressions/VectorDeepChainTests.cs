#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of deep vector expressions, built in a loop, against a BigInteger oracle computed from the host inputs:
// - evaluating, measuring (Capacity, Length, Count) and disposing such an expression does not take stack space per
//   level: chains of every node kind run on a thread with a 256 KB stack, where an evaluation that recursed once per
//   level ends the process with a stack overflow after about 80 levels (330 with 1 MB);
// - the depths around the levels evaluated by recursion (8, 24) give the same values as the deep ones, so the change
//   from recursion to the walk keeps the order of the operands;
// - an evaluation that fails deep in a chain throws the exception of the failing node and returns every buffer it
//   leased; the expression can be disposed and another one evaluated afterwards.

/// <summary>Deep vector expressions on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class VectorDeepChainTests
{
    private const int SmallStack = 256 * 1024;

    // e = e + s_k or e - s_k: every level is a scalar node over the level below
    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(20_000)]
    [Scenario(TestScenario.Boundary)]
    public void ScalarSteps_ToAnyDepth_AreExact(int depth) =>
        LimitedStack.Run(SmallStack, cpuFallback: false, workspace => VectorChains.ScalarSteps(workspace, depth));

    // e = l_k - e: every level is the right operand of a Zip node
    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(20_000)]
    [Scenario(TestScenario.Boundary)]
    public void RightDeepZips_ToAnyDepth_AreExact(int depth) =>
        LimitedStack.Run(SmallStack, cpuFallback: false, workspace =>
            VectorChains.Zips(workspace, depth, rightDeep: true));

    // e = e - l_k: every level is the left operand of a Zip node
    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(20_000)]
    [Scenario(TestScenario.Boundary)]
    public void LeftDeepZips_ToAnyDepth_AreExact(int depth) =>
        LimitedStack.Run(SmallStack, cpuFallback: false, workspace =>
            VectorChains.Zips(workspace, depth, rightDeep: false));

    // Take over Take over a Skip, evaluated into a target that every level passes down
    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(20_000)]
    [Scenario(TestScenario.Boundary)]
    public void Slices_ToAnyDepth_WriteTheTarget(int depth) =>
        LimitedStack.Run(SmallStack, cpuFallback: false, workspace => VectorChains.Slices(workspace, depth));

    // Where over Where: the count is on the device from the first level up
    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(5_000)]
    [Scenario(TestScenario.Boundary)]
    public void Wheres_ToAnyDepth_KeepTheSelection(int depth) =>
        LimitedStack.Run(SmallStack, cpuFallback: false, workspace => VectorChains.Wheres(workspace, depth));

    // ((e + s) / 2): a unary node that runs on the host over a scalar node, at every level
    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(2_000)]
    [Scenario(TestScenario.Boundary)]
    public void HalvedSums_ToAnyDepth_AreExact(int depth) =>
        LimitedStack.Run(SmallStack, cpuFallback: false, workspace => VectorChains.HalvedSums(workspace, depth));

    // Not over Not over a comparison
    [Theory]
    [InlineData(9)]
    [InlineData(25)]
    [InlineData(20_001)]
    [Scenario(TestScenario.Boundary)]
    public void Negations_ToAnyDepth_AlternateTheFlags(int depth) =>
        LimitedStack.Run(SmallStack, cpuFallback: false, workspace => VectorChains.Negations(workspace, depth));

    // b = b And c_k (left-deep) and b = c_k Or b (right-deep)
    [Theory]
    [InlineData(8, false)]
    [InlineData(8, true)]
    [InlineData(24, false)]
    [InlineData(24, true)]
    [InlineData(20_000, false)]
    [InlineData(20_000, true)]
    [Scenario(TestScenario.Boundary)]
    public void Logicals_ToAnyDepth_AreExact(int depth, bool rightDeep) =>
        LimitedStack.Run(SmallStack, cpuFallback: false, workspace =>
            VectorChains.Logicals(workspace, depth, rightDeep));

    // The float nodes: e = e + s_k or e - s_k, e = l_k - e, and Take over Take
    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(2_000)]
    [Scenario(TestScenario.Boundary)]
    public void FloatScalarSteps_ToAnyDepth_AreExact(int depth) =>
        LimitedStack.Run(SmallStack, cpuFallback: false, workspace => VectorChains.FloatScalarSteps(workspace, depth));

    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(2_000)]
    [Scenario(TestScenario.Boundary)]
    public void FloatRightDeepZips_ToAnyDepth_AreExact(int depth) =>
        LimitedStack.Run(SmallStack, cpuFallback: false, workspace => VectorChains.FloatZips(workspace, depth));

    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(20_000)]
    [Scenario(TestScenario.Boundary)]
    public void FloatSlices_ToAnyDepth_KeepTheElements(int depth) =>
        LimitedStack.Run(SmallStack, cpuFallback: false, workspace => VectorChains.FloatSlices(workspace, depth));

    // A zero divisor halfway down a chain of 20000 levels whose scalars are rented (s + s): the division throws, and
    // every buffer leased by the levels below it went back
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void FailureHalfwayDownADeepChain_ThrowsAndReturnsEveryBuffer() =>
        LimitedStack.Run(SmallStack, cpuFallback: false, static workspace =>
            VectorChains.FailureHalfwayDown(workspace, 20_000));
}

/// <summary>The chains and their oracle checks.</summary>
internal static class VectorChains
{
    private static readonly BigInteger[] s_values = [5, -7, 123_456_789, 0];

    public static void ScalarSteps(GpuWorkspace workspace, int depth)
    {
        using var array = IntArray(workspace, s_values);
        var scalars = Scalars(workspace, [3, -11, 1 << 30, 1]);
        var expected = (BigInteger[])s_values.Clone();
        var e = array.AsVectorizedEnumerable();
        try
        {
            for (var k = 0; k < depth; k++)
            {
                var s = scalars[k % scalars.Length];
                var value = ToBigInteger(s);
                e = k % 2 == 0 ? e.SelectAdd(s) : e.SelectSubtract(s);
                Update(expected, x => k % 2 == 0 ? x + value : x - value);
            }

            AssertShape(e, s_values.Length);
            Assert.Equal(expected, ReadInts(workspace, e.Evaluate(workspace)));
        }
        finally
        {
            e.Dispose();
            DisposeAll(scalars);
        }
    }

    public static void Zips(GpuWorkspace workspace, int depth, bool rightDeep)
    {
        BigInteger[][] leaves = [[1, 2, 3, 4], [-5, 6, -7, 8], [9, 0, 0, -1], [100, -200, 300, -400]];
        var arrays = leaves.Select(values => IntArray(workspace, values)).ToArray();
        var expected = (BigInteger[])s_values.Clone();
        using var start = IntArray(workspace, s_values);
        var e = start.AsVectorizedEnumerable();
        try
        {
            for (var k = 0; k < depth; k++)
            {
                var l = leaves[k % leaves.Length];
                var leaf = arrays[k % arrays.Length].AsVectorizedEnumerable();
                e = rightDeep ? leaf.ZipSubtract(e) : e.ZipSubtract(leaf);
                for (var i = 0; i < expected.Length; i++)
                {
                    expected[i] = rightDeep ? l[i] - expected[i] : expected[i] - l[i];
                }
            }

            AssertShape(e, s_values.Length);
            Assert.Equal(expected, ReadInts(workspace, e.Evaluate(workspace)));
        }
        finally
        {
            e.Dispose();
            DisposeAll(arrays);
        }
    }

    public static void Slices(GpuWorkspace workspace, int depth)
    {
        using var array = IntArray(workspace, s_values);
        using var target = new GpuBigIntArray(workspace, 3, 1);
        var e = array.AsVectorizedEnumerable().Skip(1);
        try
        {
            for (var k = 0; k < depth; k++)
            {
                e = e.Take(3 + k % 2);
            }

            AssertShape(e, 3);
            var result = e.Evaluate(workspace, 0, null, target.Values);
            Assert.Equal(s_values[1..], ReadInts(workspace, result));
            Assert.Equal(target.Values.DevicePointer, result.Values.DevicePointer);
        }
        finally
        {
            e.Dispose();
        }
    }

    public static void Wheres(GpuWorkspace workspace, int depth)
    {
        using var array = IntArray(workspace, s_values);
        using var mask = IntArray(workspace, [1, 1, 1, 0]);
        using var zero = Scalar(workspace, 0);
        var e = array.AsVectorizedEnumerable();
        GpuBigIntExpr? count = null;
        try
        {
            for (var k = 0; k < depth; k++)
            {
                e = e.Where(mask.AsVectorizedEnumerable().IsGreaterThan(zero));
            }

            Assert.Equal(s_values.Length, e.Capacity);
            Assert.False(e.TryGetLength(out _));
            Assert.Equal(s_values[..3], ReadInts(workspace, e.Evaluate(workspace)));

            // Count evaluates the vector from a scalar node; it owns the vector from here on
            count = e.Count();
            using var counted = count.Value.Node.Evaluate(workspace);
            Assert.Equal(3, ToBigInteger(counted));
        }
        finally
        {
            if (count is { } owner)
            {
                owner.Dispose();
            }
            else
            {
                e.Dispose();
            }
        }
    }

    public static void HalvedSums(GpuWorkspace workspace, int depth)
    {
        BigInteger[] start = [5, 7, 123_456_789, 0];
        using var array = IntArray(workspace, start);
        using var s = Scalar(workspace, 1_000_003);
        var expected = (BigInteger[])start.Clone();
        var e = array.AsVectorizedEnumerable();
        try
        {
            for (var k = 0; k < depth; k++)
            {
                e = k % 2 == 0 ? e.SelectAdd(s) : e.SelectDivideByUInt32(2);
                Update(expected, x => k % 2 == 0 ? x + 1_000_003 : x / 2);
            }

            AssertShape(e, start.Length);
            Assert.Equal(expected, ReadInts(workspace, e.Evaluate(workspace)));
        }
        finally
        {
            e.Dispose();
        }
    }

    public static void Negations(GpuWorkspace workspace, int depth)
    {
        using var array = IntArray(workspace, s_values);
        using var zero = Scalar(workspace, 0);
        var b = array.AsVectorizedEnumerable().IsGreaterThan(zero);
        try
        {
            for (var k = 0; k < depth; k++)
            {
                b = b.Not();
            }

            var expected = s_values.Select(x => (x > 0) != (depth % 2 == 1) ? 1u : 0u).ToArray();
            Assert.Equal(s_values.Length, b.Capacity);
            Assert.Equal(s_values.Length, b.Length);
            Assert.Equal(expected, ReadFlags(workspace, b.Evaluate(workspace)));
        }
        finally
        {
            b.Dispose();
        }
    }

    public static void Logicals(GpuWorkspace workspace, int depth, bool rightDeep)
    {
        BigInteger[][] masks = [[1, 1, 0, 1], [1, 0, 1, 1], [0, 1, 1, 1], [1, 1, 1, 0], [1, 1, 1, 1]];
        var arrays = masks.Select(values => IntArray(workspace, values)).ToArray();
        using var zero = Scalar(workspace, 0);
        var b = arrays[^1].AsVectorizedEnumerable().IsGreaterThan(zero);
        var expected = masks[^1].Select(x => x > 0).ToArray();
        try
        {
            for (var k = 0; k < depth; k++)
            {
                // Mostly true flags, so that both And and Or keep changing the result
                var j = k % 7 == 0 ? k % 4 : 4;
                var c = arrays[j].AsVectorizedEnumerable().IsGreaterThan(zero);
                var useAnd = k % 3 != 0;
                if (rightDeep)
                {
                    b = useAnd ? c.And(b) : c.Or(b);
                }
                else
                {
                    b = useAnd ? b.And(c) : b.Or(c);
                }
                for (var i = 0; i < expected.Length; i++)
                {
                    var flag = masks[j][i] > 0;
                    expected[i] = useAnd ? expected[i] && flag : expected[i] || flag;
                }
            }

            Assert.Equal(4, b.Capacity);
            Assert.Equal(expected.Select(x => x ? 1u : 0u).ToArray(), ReadFlags(workspace, b.Evaluate(workspace)));
        }
        finally
        {
            b.Dispose();
            DisposeAll(arrays);
        }
    }

    public static void FloatScalarSteps(GpuWorkspace workspace, int depth)
    {
        long[] start = [5, -7, 1_000_000, 0];
        using var array = FloatArray(workspace, start);
        var scalars = new[] { 3L, -11L, 1L << 30, 1L }.Select(x => GpuBigFloatTestHelpers.CreateFloat(workspace, x))
            .ToArray();
        var expected = start.Select(x => (BigInteger)x).ToArray();
        var e = array.AsVectorizedEnumerable();
        try
        {
            for (var k = 0; k < depth; k++)
            {
                var index = k % scalars.Length;
                var value = (BigInteger)new[] { 3L, -11L, 1L << 30, 1L }[index];
                e = k % 2 == 0 ? e.SelectAdd(scalars[index]) : e.SelectSubtract(scalars[index]);
                Update(expected, x => k % 2 == 0 ? x + value : x - value);
            }

            Assert.Equal(start.Length, e.Capacity);
            Assert.Equal(start.Length, e.Length);
            AssertFloats(e.Evaluate(workspace), expected);
        }
        finally
        {
            e.Dispose();
            DisposeAll(scalars);
        }
    }

    public static void FloatZips(GpuWorkspace workspace, int depth)
    {
        long[][] leaves = [[1, 2, 3, 4], [-5, 6, -7, 8], [9, 0, 0, -1]];
        var arrays = leaves.Select(values => FloatArray(workspace, values)).ToArray();
        long[] start = [5, -7, 1_000_000, 0];
        using var first = FloatArray(workspace, start);
        var expected = start.Select(x => (BigInteger)x).ToArray();
        var e = first.AsVectorizedEnumerable();
        try
        {
            for (var k = 0; k < depth; k++)
            {
                var l = leaves[k % leaves.Length];
                e = arrays[k % arrays.Length].AsVectorizedEnumerable().ZipSubtract(e);
                for (var i = 0; i < expected.Length; i++)
                {
                    expected[i] = l[i] - expected[i];
                }
            }

            Assert.Equal(start.Length, e.Capacity);
            AssertFloats(e.Evaluate(workspace), expected);
        }
        finally
        {
            e.Dispose();
            DisposeAll(arrays);
        }
    }

    public static void FloatSlices(GpuWorkspace workspace, int depth)
    {
        long[] start = [5, -7, 1_000_000, 0];
        using var array = FloatArray(workspace, start);
        var e = array.AsVectorizedEnumerable().Skip(1);
        try
        {
            for (var k = 0; k < depth; k++)
            {
                e = e.Take(3 + k % 2);
            }

            Assert.Equal(3, e.Capacity);
            Assert.Equal(3, e.Length);
            AssertFloats(e.Evaluate(workspace), start[1..].Select(x => (BigInteger)x).ToArray());
        }
        finally
        {
            e.Dispose();
        }
    }

    public static void FailureHalfwayDown(GpuWorkspace workspace, int depth)
    {
        using var array = IntArray(workspace, s_values);
        using var s = Scalar(workspace, 3);
        using var zero = Scalar(workspace, 0);
        // The nodes keep their result buffers until they are disposed; the rented scalars go back at once
        var before = workspace.GetMemoryStatistics().LiveBuffers;
        var e = array.AsVectorizedEnumerable();
        try
        {
            for (var k = 0; k < depth; k++)
            {
                // s + s is a compound scalar: each level rents its value and returns it after its kernel
                var sum = (GpuBigIntExpr)s + s;
                e = k == depth / 2 ? e.SelectDivide(zero) : new VectorAddScalarExpression(e, sum.Node);
            }

            Assert.Throws<DivideByZeroException>(() => e.Evaluate(workspace));
        }
        finally
        {
            e.Dispose();
        }

        Assert.Equal(before, workspace.GetMemoryStatistics().LiveBuffers);

        // The failure left nothing behind: another deep expression evaluates
        ScalarSteps(workspace, depth);
    }

    private static void AssertShape(IGpuBigIntVectorExpression e, int length)
    {
        Assert.Equal(length, e.Capacity);
        Assert.Equal(length, e.Length);
        Assert.True(e.TryGetLength(out var known));
        Assert.Equal(length, known);
    }

    private static void Update(BigInteger[] values, Func<BigInteger, BigInteger> step)
    {
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = step(values[i]);
        }
    }

    private static GpuBigIntArray IntArray(GpuWorkspace workspace, BigInteger[] values)
    {
        var words = Math.Max(1, values.Max(x => (int)((BigInteger.Abs(x).GetBitLength() + 31) / 32)));
        var array = new GpuBigIntArray(workspace, values.Length, words);
        var limbs = new uint[values.Length * words];
        var signs = new uint[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var magnitude = BigInteger.Abs(values[i]);
            for (var w = 0; w < words; w++)
            {
                limbs[i * words + w] = (uint)(magnitude >> (32 * w) & uint.MaxValue);
            }

            signs[i] = values[i] < 0 ? 1u : 0u;
        }

        workspace.Provider.CopyHostToDevice(array.Values.DevicePointer, limbs, limbs.Length);
        workspace.Provider.CopyHostToDevice(array.Signs.DevicePointer, signs, signs.Length);
        return array;
    }

    private static GpuBigFloatArray FloatArray(GpuWorkspace workspace, long[] values)
    {
        var array = new GpuBigFloatArray(workspace, values.Length, 4);
        for (var i = 0; i < values.Length; i++)
        {
            using var element = GpuBigFloatTestHelpers.CreateFloat(workspace, values[i]);
            array.SetElement(i, element);
        }

        return array;
    }

    private static GpuBigInt Scalar(GpuWorkspace workspace, BigInteger value) =>
        new(GpuBigUInt.FromBigInteger(workspace, BigInteger.Abs(value), 1), value < 0);

    private static GpuBigInt[] Scalars(GpuWorkspace workspace, BigInteger[] values) =>
        values.Select(x => Scalar(workspace, x)).ToArray();

    private static BigInteger ToBigInteger(GpuBigInt value)
    {
        var magnitude = value.Value.ToBigInteger();
        return value.IsNegative ? -magnitude : magnitude;
    }

    private static BigInteger[] ReadInts(GpuWorkspace workspace, GpuBigIntArrayView view)
    {
        var words = view.WordsPerElement;
        var limbs = new uint[view.Length * words];
        var signs = new uint[view.Length];
        workspace.Provider.CopyDeviceToHost(limbs, view.Values.DevicePointer, limbs.Length);
        workspace.Provider.CopyDeviceToHost(signs, view.Signs.DevicePointer, signs.Length);
        var result = new BigInteger[view.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var magnitude = BigInteger.Zero;
            for (var w = words - 1; w >= 0; w--)
            {
                magnitude = (magnitude << 32) | limbs[i * words + w];
            }

            result[i] = signs[i] != 0 ? -magnitude : magnitude;
        }

        return result;
    }

    private static uint[] ReadFlags(GpuWorkspace workspace, GpuBigUIntArrayView view)
    {
        var flags = new uint[view.Length];
        workspace.Provider.CopyDeviceToHost(flags, view.DevicePointer, flags.Length);
        return flags;
    }

    private static void AssertFloats(GpuBigFloatArrayView view, BigInteger[] expected)
    {
        Assert.Equal(expected.Length, view.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            GpuBigFloatTestHelpers.AssertElement(view.GetElement(i), expected[i], 1, $"element {i}");
        }
    }

    private static void DisposeAll<T>(T[] items)
        where T : IDisposable
    {
        foreach (var item in items)
        {
            item.Dispose();
        }
    }
}
