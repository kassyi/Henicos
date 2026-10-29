#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// A vector expression evaluated into a target that shares memory with one of its inputs (#864): the node computes into
/// its own cache and copies the result into the target, as the scalar roots do (#636), so that no kernel or host loop
/// writes an element that another element still has to read.
/// </summary>
/// <remarks>
/// <para>
/// The overlapping layouts are built by placing the input and the target in one buffer: the same words (in place), the
/// target one input element after the input (element i lands on input element i + 1, which is read later), and the
/// target one input element before it. The disjoint layout is the control. A target of another width than the input
/// (SelectAdd, SelectMultiply) is reachable through the public API only with a node's lent cache as the target; the
/// test reinterprets one buffer instead, which gives the node the same pointers.
/// </para>
/// <para>
/// The integer results are compared with <see cref="BigInteger"/> computed from the host input. The floating-point
/// results are compared word for word with the same expression evaluated into a disjoint target from a pristine copy of
/// the input (the floating-point values themselves are checked by <c>GpuBigFloatLinqAndVectorTests</c>).
/// </para>
/// <para>
/// mutant: <c>DeviceRanges.Overlaps(GpuBigUIntArrayView, GpuBigUIntArrayView)</c> and
/// <c>DeviceRanges.Overlaps(GpuBigFloatArrayView, GpuBigIntMemoryHandle)</c> return <see langword="false"/>: the
/// TargetAhead case of every node except the leaves fails (with most InPlace / TargetBehind cases, 87 to 88 on an
/// RTX 3070 Ti), and so does the public SelectDivide reproduction. The leaves have no failing witness: the driver copy
/// of a partly overlapping range happened to be right there, and the staging copy follows the <c>cuMemcpyDtoD</c>
/// contract, which does not promise it.
/// </para>
/// </remarks>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class VectorTargetOverlapTests : IDisposable
{
    private const int Seed = 864;

    // Element-wise kernels: enough blocks that the blocks of other elements run while one element is written
    private const int KernelCount = 1024;
    private const int KernelWords = 16;

    // Host loops (one or more launches per element): the overwrite is deterministic, a few elements suffice
    private const int HostCount = 8;
    private const int HostWords = 4;

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public VectorTargetOverlapTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    public enum Layout
    {
        Disjoint,
        InPlace,
        TargetAhead,
        TargetBehind
    }

    public enum Operand
    {
        Left,
        Right
    }

    public enum IntOp
    {
        Leaf,
        SelectAdd,
        SelectSubtract,
        SelectMultiply,
        SelectDivide,
        SelectModulo,
        SelectSqrt,
        SelectDivideByUInt32,
        ZipAdd,
        ZipSubtract,
        ZipMultiply,
        ZipDivide,
        ZipModulo
    }

    public enum FloatOp
    {
        Leaf,
        SelectAdd,
        SelectSubtract,
        SelectMultiply,
        SelectDivide,
        SelectPow,
        SelectSqrt,
        SelectRsqrt,
        SelectReciprocal,
        SelectDivideByUInt32,
        SelectExp,
        SelectLog,
        ZipAdd,
        ZipSubtract,
        ZipMultiply,
        ZipDivide,
        ZipPow
    }

    public static TheoryData<IntOp, Layout, Operand> IntCases()
    {
        var data = new TheoryData<IntOp, Layout, Operand>();
        foreach (var op in Enum.GetValues<IntOp>())
        {
            foreach (var layout in Enum.GetValues<Layout>())
            {
                data.Add(op, layout, Operand.Left);
                if (IsZip(op) && layout != Layout.Disjoint)
                {
                    data.Add(op, layout, Operand.Right);
                }
            }
        }

        return data;
    }

    public static TheoryData<FloatOp, Layout, Operand> FloatCases()
    {
        var data = new TheoryData<FloatOp, Layout, Operand>();
        foreach (var op in Enum.GetValues<FloatOp>())
        {
            foreach (var layout in Enum.GetValues<Layout>())
            {
                data.Add(op, layout, Operand.Left);
                if (IsZip(op) && layout != Layout.Disjoint)
                {
                    data.Add(op, layout, Operand.Right);
                }
            }
        }

        return data;
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(IntCases))]
    public void Int_TargetOverlappingAnInput_ReceivesTheExactResult(IntOp op, Layout layout, Operand overlapped)
    {
        var (count, words) = IsHostLoop(op) ? (HostCount, HostWords) : (KernelCount, KernelWords);
        var random = new Random(Seed + (int)op * 97 + (int)layout * 7 + (int)overlapped);
        // The products are kept below 2^(32 * words): the multiply kernels drop the carry out of the left operand's top
        // word (a separate bug), which this test is not about
        var leftBits = op is IntOp.SelectMultiply or IntOp.ZipMultiply ? words * 16 : words * 32 - 2;
        var left = RandomValues(random, count, leftBits);
        var right = RandomValues(random, count, words * 16).Select(static v => v | 1).ToArray();
        var scalarValue = RandomValue(random, words * 16) | 1;
        var divisor = (uint)random.Next(3, int.MaxValue) | 1u;

        // A buffer large enough for an input and a target of up to twice the input width, either one shifted by one
        // input element
        var storageWords = (count + 1) * 2 * words + words;
        using var storage = new GpuBigUIntArray(_workspace, storageWords, 1);
        using var separate = new GpuBigUIntArray(_workspace, storageWords, 1);
        var (inputOffset, targetOffset) = Offsets(layout, words);

        using var leftArray = overlapped == Operand.Left
            ? Place(storage, inputOffset, left, words)
            : Place(separate, 0, left, words);
        using var rightArray = overlapped == Operand.Right
            ? Place(storage, inputOffset, right, words)
            : Place(separate, (count + 1) * words, right, words);
        using var scalar = new GpuBigInt(GpuBigUInt.FromBigInteger(_workspace, scalarValue, words), false);
        using var expression = CreateIntNode(op, leftArray, rightArray, scalar, divisor);
        using var disjointTarget = new GpuBigUIntArray(_workspace, count, expression.WordsPerElement);
        var target = layout == Layout.Disjoint
            ? (GpuBigUIntArrayView)disjointTarget
            : new GpuBigUIntArrayView(_workspace, storage.Handle.Slice(targetOffset, count * expression.WordsPerElement),
                count, expression.WordsPerElement);

        var result = expression.Evaluate(_workspace, 0, count, target);

        // Each element is the operation on the host input: an element written before another element read its input
        // would give a wrong value there.
        // mutant: DeviceRanges.Overlaps(GpuBigUIntArrayView, GpuBigUIntArrayView) `=> false` (every non-leaf
        // TargetAhead case fails)
        var expected = ExpectedInt(op, left, right, scalarValue, divisor);
        Assert.Equal(expected, ReadSigned(result));
        // The values are delivered in the caller's target, also when the node computed them in its cache
        Assert.Equal(target.DevicePointer, result.Values.DevicePointer);
        Assert.Equal(expected.Select(BigInteger.Abs), ReadValues(target));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(FloatCases))]
    public void Float_TargetOverlappingAnInput_ReceivesTheDisjointResult(FloatOp op, Layout layout, Operand overlapped)
    {
        var (count, words) = IsHostLoop(op) ? (HostCount, HostWords) : (KernelCount, HostWords);
        var random = new Random(Seed + (int)op * 89 + (int)layout * 5 + (int)overlapped);
        // Small positive integers: in the domain of every operation (Log, Rsqrt, Reciprocal, Pow)
        var left = Enumerable.Range(0, count).Select(_ => (uint)random.Next(1, 64)).ToArray();
        var right = Enumerable.Range(0, count).Select(_ => (uint)random.Next(1, 4)).ToArray();

        // The reference: the same expression on a pristine copy, into a disjoint target
        uint[][] expected;
        {
            using var pristineLeft = CreateFloatArray(left, words);
            using var pristineRight = CreateFloatArray(right, words);
            using var scalar = CreateFloat(2, words);
            using var reference = CreateFloatNode(op, pristineLeft, pristineRight, scalar);
            using var referenceTarget = new GpuBigFloatArray(_workspace, count, reference.WordsPerElement);
            reference.Evaluate(_workspace, 0, count, referenceTarget);
            expected = ReadFloatWords(referenceTarget);
        }

        var outWords = 2 * words + 1;
        using var significands = new GpuBigUIntArray(_workspace, (count + 1) * outWords, 1);
        using var exponents = new GpuBigUIntArray(_workspace, count + 1, 1);
        using var signs = new GpuBigUIntArray(_workspace, count + 1, 1);
        var (inputOffset, targetOffset) = Offsets(layout, words);
        var (inputScalarOffset, targetScalarOffset) = Offsets(layout, 1);

        using var overlappedArray = PlaceFloat(significands, exponents, signs, inputOffset, inputScalarOffset,
            overlapped == Operand.Left ? left : right, words);
        using var otherArray = CreateFloatArray(overlapped == Operand.Left ? right : left, words);
        using var floatScalar = CreateFloat(2, words);
        using var expression = overlapped == Operand.Left
            ? CreateFloatNode(op, overlappedArray, otherArray, floatScalar)
            : CreateFloatNode(op, otherArray, overlappedArray, floatScalar);
        var width = expression.WordsPerElement;
        using var disjointTarget = new GpuBigFloatArray(_workspace, count, width);
        var target = layout == Layout.Disjoint
            ? (GpuBigFloatArrayView)disjointTarget
            : new GpuBigFloatArrayView(
                new GpuBigUIntArrayView(_workspace, significands.Handle.Slice(targetOffset, count * width), count, width),
                new GpuBigUIntArrayView(_workspace, exponents.Handle.Slice(targetScalarOffset, count), count, 1),
                new GpuBigUIntArrayView(_workspace, signs.Handle.Slice(targetScalarOffset, count), count, 1));

        var result = expression.Evaluate(_workspace, 0, count, target);

        // Same words as the disjoint evaluation: significands, exponents and signs.
        // mutant: DeviceRanges.Overlaps(GpuBigFloatArrayView, GpuBigIntMemoryHandle) `=> false` (every non-leaf
        // TargetAhead case fails)
        Assert.Equal(target.Significands.DevicePointer, result.Significands.DevicePointer);
        var actual = ReadFloatWords(target);
        Assert.Equal(expected[0], actual[0]);
        Assert.Equal(expected[1], actual[1]);
        Assert.Equal(expected[2], actual[2]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Int_ScalarInsideTheTarget_IsReadBeforeItIsOverwritten()
    {
        // The divisor of SelectDivide lies in the words of the target's last element: every block reads it, so a block
        // that writes its element first would change the divisor of the others.
        // mutant: the scalar operand left out of the overlap check
        const int count = KernelCount;
        const int words = KernelWords;
        var random = new Random(Seed);
        var left = RandomValues(random, count, words * 32 - 2);
        var divisor = RandomValue(random, words * 16) | 1;
        using var storage = new GpuBigUIntArray(_workspace, count * words, 1);
        using var input = CreateIntArray(left, words);
        var scalarHandle = storage.Handle.Slice((count - 1) * words, words);
        WriteWords(scalarHandle.DevicePointer, [divisor], words);
        using var scalar = new GpuBigInt(scalarHandle.AsBorrowedBigUInt(_workspace), false);
        using var expression = input.AsVectorizedEnumerable().SelectDivide(scalar);
        var target = new GpuBigUIntArrayView(_workspace, storage.Handle, count, words);

        var result = expression.Evaluate(_workspace, 0, count, target);

        Assert.Equal(left.Select(v => v / divisor), ReadSigned(result));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void IntLeaf_TargetOverTheInputSigns_KeepsTheSigns()
    {
        // With 1-word elements the public Signs array of the input passes the shape check as a target: the leaf writes
        // the magnitudes over the signs it returns, so it keeps a copy of the signs first.
        // mutant: GpuBigIntVectorLeafExpression without the signs staging (the magnitudes are read back as signs)
        const int count = 64;
        var values = Enumerable.Range(0, count).Select(static i => new BigInteger(i % 3 == 0 ? -(i + 7) : i + 7))
            .ToArray();
        using var array = CreateSignedArray(values);
        using var expression = array.AsVectorizedEnumerable();
        GpuBigUIntArrayView signs = array.Signs;

        var result = expression.Evaluate(_workspace, 0, count, signs);

        Assert.Equal(values, ReadSigned(result));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Int_TargetOverTheInputSigns_ReceivesTheExactResult()
    {
        // The target is the input's own signs shifted by one element: the kernel would write a magnitude over the sign
        // another block still reads, so the overlap check covers the signs of an input, not only its values.
        // mutant: DeviceRanges.Overlaps(GpuBigUIntArrayView, GpuBigIntArrayView) without `|| Overlaps(a, b.Signs)`
        // (signs flipped)
        const int count = 65536;
        var random = new Random(Seed);
        var values = Enumerable.Range(0, count).Select(_ => new BigInteger(random.Next(0, 1 << 30))).ToArray();
        using var array = CreateSignedArray(values);
        using var scalar = new GpuBigInt(GpuBigUInt.FromBigInteger(_workspace, 5, 1), false);
        using var expression = array.AsVectorizedEnumerable().SelectSubtract(scalar);
        GpuBigUIntArrayView signs = array.Signs;

        var result = expression.Evaluate(_workspace, 0, count - 1, signs.Slice(1, count - 1));

        Assert.Equal(values.Take(count - 1).Select(static v => v - 5), ReadSigned(result));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Float_ScalarInsideTheTarget_IsReadBeforeItIsOverwritten()
    {
        // The float divisor's significand lies in the words of the target's last element (float side of the test above).
        // mutant: the scalar operand left out of the overlap check of FloatVectorBinaryScalarExpressionBase
        const int count = KernelCount;
        const int words = HostWords;
        var random = new Random(Seed);
        var values = Enumerable.Range(0, count).Select(_ => (uint)random.Next(1, 1 << 20)).ToArray();

        uint[][] expected;
        {
            using var pristine = CreateFloatArray(values, words);
            using var divisor = CreateFloat(3, words);
            using var reference = pristine.AsVectorizedEnumerable().SelectDivide(divisor);
            using var referenceTarget = new GpuBigFloatArray(_workspace, count, reference.WordsPerElement);
            reference.Evaluate(_workspace, 0, count, referenceTarget);
            expected = ReadFloatWords(referenceTarget);
        }

        using var input = CreateFloatArray(values, words);
        int width;
        using (var probeDivisor = CreateFloat(3, words))
        using (var probe = input.AsVectorizedEnumerable().SelectDivide(probeDivisor))
        {
            width = probe.WordsPerElement;
        }

        using var significands = new GpuBigUIntArray(_workspace, count * width, 1);
        using var exponents = new GpuBigUIntArray(_workspace, count, 1);
        using var signs = new GpuBigUIntArray(_workspace, count, 1);
        var scalarHandle = significands.Handle.Slice((count - 1) * width, words);
        WriteWords(scalarHandle.DevicePointer, [3], words);
        using var scalar = new GpuBigFloat(scalarHandle.AsBorrowedBigUInt(_workspace), 0, false);
        using var expression = input.AsVectorizedEnumerable().SelectDivide(scalar);
        var target = new GpuBigFloatArrayView(
            new GpuBigUIntArrayView(_workspace, significands.Handle, count, width),
            new GpuBigUIntArrayView(_workspace, exponents.Handle, count, 1),
            new GpuBigUIntArrayView(_workspace, signs.Handle, count, 1));

        expression.Evaluate(_workspace, 0, count, target);

        var actual = ReadFloatWords(target);
        Assert.Equal(expected[0], actual[0]);
        Assert.Equal(expected[1], actual[1]);
        Assert.Equal(expected[2], actual[2]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Int_PublicSliceOfTheInput_AsTarget_ReceivesTheExactResult(bool subtract)
    {
        // The reproduction of #864 through the public API only: element i of the input written to slot i + 1 of the
        // same array.
        // mutant: the overlap check removed (SelectDivide: almost every quotient wrong; SelectSubtract: racy)
        const int count = 2000;
        const int words = 64;
        var random = new Random(Seed + (subtract ? 1 : 0));
        var values = RandomValues(random, count, words * 32 - 40);
        var scalarValue = new BigInteger(12345);
        using var array = CreateIntArray(values, words);
        using var scalar = new GpuBigInt(GpuBigUInt.FromBigInteger(_workspace, scalarValue, words), false);
        using var expression = subtract
            ? array.AsVectorizedEnumerable().SelectSubtract(scalar)
            : array.AsVectorizedEnumerable().SelectDivide(scalar);
        GpuBigUIntArrayView arrayValues = array.Values;

        var result = expression.Evaluate(_workspace, 0, count - 1, arrayValues.Slice(1, count - 1));

        var expected = values.Take(count - 1).Select(v => subtract ? v - scalarValue : v / scalarValue);
        Assert.Equal(expected, ReadSigned(result));
    }

    private static bool IsZip(IntOp op) => op >= IntOp.ZipAdd;

    private static bool IsZip(FloatOp op) => op >= FloatOp.ZipAdd;

    private static bool IsHostLoop(IntOp op) => op is IntOp.SelectSqrt or IntOp.SelectDivideByUInt32;

    private static bool IsHostLoop(FloatOp op) =>
        op is FloatOp.SelectPow or FloatOp.SelectSqrt or FloatOp.SelectRsqrt or FloatOp.SelectReciprocal
            or FloatOp.SelectDivideByUInt32 or FloatOp.SelectExp or FloatOp.SelectLog or FloatOp.ZipPow;

    // (input, target) word offsets in the shared buffer
    private static (int Input, int Target) Offsets(Layout layout, int inputWords) =>
        layout switch
        {
            Layout.TargetAhead => (0, inputWords),
            Layout.TargetBehind => (inputWords, 0),
            _ => (0, 0)
        };

    private static IGpuBigIntVectorExpression CreateIntNode(
        IntOp op, GpuBigIntArray left, GpuBigIntArray right, GpuBigInt scalar, uint divisor)
    {
        var l = left.AsVectorizedEnumerable();
        var r = right.AsVectorizedEnumerable();
        return op switch
        {
            IntOp.Leaf => l,
            IntOp.SelectAdd => l.SelectAdd(scalar),
            IntOp.SelectSubtract => l.SelectSubtract(scalar),
            IntOp.SelectMultiply => l.SelectMultiply(scalar),
            IntOp.SelectDivide => l.SelectDivide(scalar),
            IntOp.SelectModulo => l.SelectModulo(scalar),
            IntOp.SelectSqrt => l.SelectSqrt(),
            IntOp.SelectDivideByUInt32 => l.SelectDivideByUInt32(divisor),
            IntOp.ZipAdd => l.ZipAdd(r),
            IntOp.ZipSubtract => l.ZipSubtract(r),
            IntOp.ZipMultiply => l.ZipMultiply(r),
            IntOp.ZipDivide => l.ZipDivide(r),
            IntOp.ZipModulo => l.ZipModulo(r),
            _ => throw new ArgumentOutOfRangeException(nameof(op))
        };
    }

    private static BigInteger[] ExpectedInt(
        IntOp op, BigInteger[] left, BigInteger[] right, BigInteger scalar, uint divisor) =>
        left.Select((a, i) =>
        {
            var b = right[i];
            return op switch
            {
                IntOp.Leaf => a,
                IntOp.SelectAdd => a + scalar,
                IntOp.SelectSubtract => a - scalar,
                IntOp.SelectMultiply => a * scalar,
                IntOp.SelectDivide => a / scalar,
                IntOp.SelectModulo => a % scalar,
                IntOp.SelectSqrt => IntegerSqrt(a),
                IntOp.SelectDivideByUInt32 => a / divisor,
                IntOp.ZipAdd => a + b,
                IntOp.ZipSubtract => a - b,
                IntOp.ZipMultiply => a * b,
                IntOp.ZipDivide => a / b,
                IntOp.ZipModulo => a % b,
                _ => throw new ArgumentOutOfRangeException(nameof(op))
            };
        }).ToArray();

    private static IGpuBigFloatVectorExpression CreateFloatNode(
        FloatOp op, GpuBigFloatArray left, GpuBigFloatArray right, GpuBigFloat scalar)
    {
        var l = left.AsVectorizedEnumerable();
        var r = right.AsVectorizedEnumerable();
        return op switch
        {
            FloatOp.Leaf => l,
            FloatOp.SelectAdd => l.SelectAdd(scalar),
            FloatOp.SelectSubtract => l.SelectSubtract(scalar),
            FloatOp.SelectMultiply => l.SelectMultiply(scalar),
            FloatOp.SelectDivide => l.SelectDivide(scalar),
            FloatOp.SelectPow => l.SelectPow(scalar),
            FloatOp.SelectSqrt => l.SelectSqrt(),
            FloatOp.SelectRsqrt => l.SelectRsqrt(),
            FloatOp.SelectReciprocal => l.SelectReciprocal(),
            FloatOp.SelectDivideByUInt32 => l.SelectDivideByUInt32(3),
            FloatOp.SelectExp => l.SelectExp(),
            FloatOp.SelectLog => l.SelectLog(),
            FloatOp.ZipAdd => l.ZipAdd(r),
            FloatOp.ZipSubtract => l.ZipSubtract(r),
            FloatOp.ZipMultiply => l.ZipMultiply(r),
            FloatOp.ZipDivide => l.ZipDivide(r),
            FloatOp.ZipPow => l.ZipPow(r),
            _ => throw new ArgumentOutOfRangeException(nameof(op))
        };
    }

    private static BigInteger IntegerSqrt(BigInteger value)
    {
        if (value < 2)
        {
            return value;
        }

        var x = BigInteger.One << (int)((value.GetBitLength() + 1) / 2);
        while (true)
        {
            var y = (x + value / x) >> 1;
            if (y >= x)
            {
                return x;
            }

            x = y;
        }
    }

    private static BigInteger RandomValue(Random random, int bits)
    {
        var bytes = new byte[(bits + 7) / 8 + 1];
        random.NextBytes(bytes);
        bytes[^1] = 0;
        return new BigInteger(bytes) & ((BigInteger.One << bits) - 1);
    }

    private static BigInteger[] RandomValues(Random random, int count, int bits) =>
        Enumerable.Range(0, count).Select(_ => RandomValue(random, bits)).ToArray();

    // A non-negative array whose values are the words of storage from wordOffset; its signs are an array of its own
    private GpuBigIntArray Place(GpuBigUIntArray storage, int wordOffset, BigInteger[] values, int words)
    {
        var handle = storage.Handle.Slice(wordOffset, values.Length * words);
        WriteWords(handle.DevicePointer, values, words);
        var signs = new GpuBigUIntArray(_workspace, values.Length, 1);
        _workspace.Provider.ClearMemoryAsync(signs.DevicePointer, values.Length, _workspace.Stream);
        return new GpuBigIntArray(new GpuBigUIntArray(_workspace, handle, values.Length, words), signs);
    }

    private GpuBigIntArray CreateIntArray(BigInteger[] values, int words)
    {
        var array = new GpuBigIntArray(_workspace, values.Length, words);
        WriteWords(array.Values.DevicePointer, values, words);
        _workspace.Provider.ClearMemoryAsync(array.Signs.DevicePointer, values.Length, _workspace.Stream);
        return array;
    }

    // A 1-word signed array
    private GpuBigIntArray CreateSignedArray(BigInteger[] values)
    {
        var array = new GpuBigIntArray(_workspace, values.Length, 1);
        var magnitudes = values.Select(BigInteger.Abs).ToArray();
        var signs = values.Select(static v => v.Sign < 0 ? 1u : 0u).ToArray();
        WriteWords(array.Values.DevicePointer, magnitudes, 1);
        _workspace.Provider.CopyHostToDevice(array.Signs.DevicePointer, signs, signs.Length, _workspace.Stream);
        return array;
    }

    // A float array of small integers (exponent 0, positive) whose components are the words of the three buffers
    private GpuBigFloatArray PlaceFloat(GpuBigUIntArray significands, GpuBigUIntArray exponents, GpuBigUIntArray signs,
        int wordOffset, int scalarOffset, uint[] values, int words)
    {
        var count = values.Length;
        var array = new GpuBigFloatArray(
            new GpuBigUIntArray(_workspace, significands.Handle.Slice(wordOffset, count * words), count, words),
            new GpuBigUIntArray(_workspace, exponents.Handle.Slice(scalarOffset, count), count, 1),
            new GpuBigUIntArray(_workspace, signs.Handle.Slice(scalarOffset, count), count, 1));
        WriteFloat(array, values, words);
        return array;
    }

    private GpuBigFloatArray CreateFloatArray(uint[] values, int words)
    {
        var array = new GpuBigFloatArray(_workspace, values.Length, words);
        WriteFloat(array, values, words);
        return array;
    }

    private void WriteFloat(GpuBigFloatArray array, uint[] values, int words)
    {
        var host = new uint[values.Length * words];
        for (var i = 0; i < values.Length; i++)
        {
            host[i * words] = values[i];
        }

        _workspace.Provider.CopyHostToDevice(array.Significands.DevicePointer, host, host.Length, _workspace.Stream);
        _workspace.Provider.ClearMemoryAsync(array.Exponents.DevicePointer, values.Length, _workspace.Stream);
        _workspace.Provider.ClearMemoryAsync(array.Signs.DevicePointer, values.Length, _workspace.Stream);
    }

    private GpuBigFloat CreateFloat(uint value, int words)
    {
        var host = new uint[words];
        host[0] = value;
        return new GpuBigFloat(new GpuBigUInt(_workspace, host), 0, false);
    }

    private void WriteWords(IntPtr destination, BigInteger[] values, int words)
    {
        var host = new uint[values.Length * words];
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            for (var w = 0; w < words; w++)
            {
                host[i * words + w] = (uint)(value & uint.MaxValue);
                value >>= 32;
            }
        }

        _workspace.Provider.CopyHostToDevice(destination, host, host.Length, _workspace.Stream);
    }

    private uint[][] ReadFloatWords(GpuBigFloatArrayView array) =>
    [
        ReadWords(array.Significands), ReadWords(array.Exponents), ReadWords(array.Signs)
    ];

    private uint[] ReadWords(GpuBigUIntArrayView values)
    {
        var host = new uint[values.TotalWords];
        _workspace.Provider.CopyDeviceToHost(host, values.DevicePointer, host.Length, _workspace.Stream);
        return host;
    }

    private BigInteger[] ReadSigned(GpuBigIntArrayView array)
    {
        var values = ReadValues(array.Values);
        var signs = ReadWords(array.Signs);
        return values.Select((v, i) => signs[i] != 0 ? -v : v).ToArray();
    }

    private BigInteger[] ReadValues(GpuBigUIntArrayView values)
    {
        var host = ReadWords(values);
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
