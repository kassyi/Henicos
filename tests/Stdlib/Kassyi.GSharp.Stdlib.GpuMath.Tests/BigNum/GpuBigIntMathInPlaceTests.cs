#nullable enable
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// The public statics of <see cref="GpuBigIntMath"/> called with the result buffer as one of the operands (#701): the
/// result is the one of separate buffers, also when the launch has many blocks.
/// </summary>
/// <remarks>
/// The expected values come from <see cref="BigInteger"/> on the values the test generated (fixed seeds, shown in the
/// failure message). A kernel that reads words other threads write races only on the GPU, so a wrong result shows only
/// there, and not on every run; the large cases make it near certain (a left shift by <c>32 * 4096 + 5</c> bits of
/// 65536 words went wrong 10 times out of 10 without the overlap handling).
/// </remarks>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class GpuBigIntMathInPlaceTests : IDisposable
{
    // 65536 words: 256 blocks of the grid kernels, 64 passes of a single-block kernel
    private const int LargeWords = 65536;

    // One pass of a 1024-thread single-block kernel
    private const int SmallWords = 1000;

    // The largest operand that a shift runs in place in one block; a larger one is copied first
    private const int InPlaceMaxWords = KernelBasicMathHandler.InPlaceShiftMaxWords;

    // A warp, a block of 1024 threads and two passes of it, each with its neighbours
    private static readonly int[] BoundaryWords = [31, 32, 33, 1023, 1024, 1025, 2047, 2048, 2049];

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBigIntMathInPlaceTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(4 * LargeWords, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    public enum Operation
    {
        ShiftLeft,
        ShiftRight,
        Add,
        AddScalar,
        Sub,
        SubScalar,
        MulScalar,
        BitwiseAnd,
        BitwiseOr,
        BitwiseXor,
        BitwiseNot,
        DivideByUInt32
    }

    /// <summary>
    /// The operation, the word count, the shift in bits (shifts only) and which operand is the result.
    /// </summary>
    public static TheoryData<Operation, int, int, bool> InPlaceCases()
    {
        var data = new TheoryData<Operation, int, int, bool>
        {
            // The shift of the issue: a word shift of 4096 crosses 16 blocks of 256 threads
            { Operation.ShiftLeft, LargeWords, (32 * 4096) + 5, false },
            { Operation.ShiftRight, LargeWords, (32 * 4096) + 5, false },

            // A bit shift only: each word reads its neighbour, which the next thread writes
            { Operation.ShiftLeft, LargeWords, 5, false },
            { Operation.ShiftRight, LargeWords, 5, false },

            // A whole-word shift (no bit part) and a shift of a few words
            { Operation.ShiftLeft, LargeWords, 64, false },
            { Operation.ShiftRight, LargeWords, 101, false },

            // The largest operand shifted in place by one block (32 passes), and one word above it (a copy)
            { Operation.ShiftLeft, InPlaceMaxWords, (32 * 4096) + 5, false },
            { Operation.ShiftRight, InPlaceMaxWords, (32 * 4096) + 5, false },
            { Operation.ShiftLeft, InPlaceMaxWords + 1, (32 * 4096) + 5, false },
            { Operation.ShiftRight, InPlaceMaxWords + 1, (32 * 4096) + 5, false },

            // Small values, one pass of a block
            { Operation.ShiftLeft, SmallWords, 37, false },
            { Operation.ShiftRight, SmallWords, 37, false },
            { Operation.ShiftLeft, SmallWords, (32 * 300) + 7, false },
            { Operation.ShiftRight, SmallWords, (32 * 300) + 7, false },
            { Operation.ShiftLeft, 1, 3, false },
            { Operation.ShiftRight, 1, 3, false },

            // A shift by zero copies the value onto itself
            { Operation.ShiftLeft, LargeWords, 0, false },
            { Operation.ShiftRight, LargeWords, 0, false }
        };

        // Sizes at a warp, a block of 1024 threads and two passes of it, each shifted by multiples of 32 and by the
        // width and its neighbours (a shift by the width or more gives zero)
        foreach (var words in BoundaryWords)
        {
            var width = 32 * words;
            foreach (var shift in new[] { 32, 64, width - 1, width, width + 1 })
            {
                data.Add(Operation.ShiftLeft, words, shift, false);
                data.Add(Operation.ShiftRight, words, shift, false);
            }
        }

        foreach (var operation in new[]
                 {
                     Operation.Add, Operation.Sub, Operation.BitwiseAnd, Operation.BitwiseOr, Operation.BitwiseXor
                 })
        {
            foreach (var words in new[] { SmallWords, LargeWords })
            {
                data.Add(operation, words, 0, false);
                data.Add(operation, words, 0, true);
            }
        }

        foreach (var operation in new[]
                 {
                     Operation.AddScalar, Operation.SubScalar, Operation.MulScalar, Operation.BitwiseNot,
                     Operation.DivideByUInt32
                 })
        {
            data.Add(operation, SmallWords, 0, false);
            data.Add(operation, LargeWords, 0, false);
        }

        return data;
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(InPlaceCases))]
    public void Operation_ResultIsAnOperand_GivesTheSeparateBufferResult(Operation operation, int words, int shift,
        bool resultIsRight)
    {
        // P1: the value in the result buffer equals the BigInteger result of the original operands, as with a separate
        // result buffer.
        // P2: the operand that is not the result keeps its value.
        // mutant: KernelBasicMathHandler.Shift without the overlap branch (the grid kernel runs in place): ShiftLeft
        //         LargeWords by 64 and by 32 * 4096 + 5, and ShiftRight LargeWords by 32 * 4096 + 5 fail
        // A missing barrier in the in-place kernels fails here only now and then; Shift_InPlaceManyTimes catches it.
        var seed = 701 + ((int)operation * 1_000_003) + (words * 31) + shift + (resultIsRight ? 7 : 0);
        var random = new Random(seed);
        var (a0, b0) = Operands(operation, words, shift, random);
        const uint Scalar = 0x9E3779B9u;

        using var a = GpuBigUInt.FromBigInteger(_workspace, a0, words);
        using var b = GpuBigUInt.FromBigInteger(_workspace, b0, words);
        var result = resultIsRight ? b : a;

        var expected = Run(operation, a, b, result, shift, Scalar, a0, b0, words);

        var message = $"{operation} words {words} shift {shift} result is {(resultIsRight ? "right" : "left")}, " +
                      $"seed {seed}";
        Assert.True(expected == result.ToBigInteger(), $"result: {message}");
        var (untouched, untouchedExpected) = resultIsRight ? (a, a0) : (b, b0);
        Assert.True(untouchedExpected == untouched.ToBigInteger(), $"other operand changed: {message}");
    }

    public enum PartialOverlap
    {
        // ShiftLeft from words [0, n) into words [1, n + 1): the grid kernel through a copy
        LeftIntoTheWordsAbove,

        // ShiftRight from words [1, n + 1) into words [0, n): the grid kernel through a copy
        RightIntoTheWordsBelow,

        // ShiftLeft from words [1, n + 1) into the n + 1 words [0, n + 1): a wider result, ShiftAndResize
        LeftIntoAWiderBufferBelow
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(PartialOverlap.LeftIntoTheWordsAbove, LargeWords, (32 * 4096) + 5)]
    [InlineData(PartialOverlap.RightIntoTheWordsBelow, LargeWords, (32 * 4096) + 5)]
    [InlineData(PartialOverlap.LeftIntoAWiderBufferBelow, 2048, 7)]
    public void Shift_ResultPartlyOverlapsTheOperand_GivesTheSeparateBufferResult(PartialOverlap overlap, int words,
        int shift)
    {
        // P1: the shifted value lands in the result words as from a separate operand, and the word of the buffer
        // outside the result keeps its value.
        // mutant: KernelBasicMathHandler.Shift `!DeviceRanges.Overlaps(...)` -> `resultBuffer.DevicePointer !=
        //         operand.DevicePointer` (a partial overlap runs the grid kernel in place): LeftIntoTheWordsAbove fails
        // mutant: the same branch removed: LeftIntoTheWordsAbove and RightIntoTheWordsBelow fail
        // Without the copy of ShiftAndResizeCore, LeftIntoAWiderBufferBelow is a race that did not show on an RTX 3070
        // Ti; ShiftLeft_WiderResultOneBlockAboveTheOperand is the case that fails without it.
        var seed = 7010 + ((int)overlap * 97) + words;
        var random = new Random(seed);
        var wordBits = 32 * words;
        var sourceBits = overlap == PartialOverlap.LeftIntoTheWordsAbove ? wordBits - shift : wordBits;
        var source0 = RandomValue(random, sourceBits);
        var other0 = RandomValue(random, 32);
        var onTop = overlap != PartialOverlap.LeftIntoTheWordsAbove;
        var buffer0 = onTop ? (source0 << 32) | other0 : (other0 << wordBits) | source0;

        using var buffer = GpuBigUInt.FromBigInteger(_workspace, buffer0, words + 1);
        GpuBigUIntView view = buffer;
        BigInteger expected;
        switch (overlap)
        {
            case PartialOverlap.LeftIntoTheWordsAbove:
                GpuBigIntMath.ShiftLeft(view.Slice(0, words), shift, view.Slice(1, words));
                expected = ((source0 << shift) << 32) | (buffer0 & uint.MaxValue);
                break;
            case PartialOverlap.RightIntoTheWordsBelow:
                GpuBigIntMath.ShiftRight(view.Slice(1, words), shift, view.Slice(0, words));
                expected = (source0 >> shift) | (source0 >> (wordBits - 32) << wordBits);
                break;
            default:
                GpuBigIntMath.ShiftLeft(view.Slice(1, words), shift, view);
                expected = source0 << shift;
                break;
        }

        Assert.True(expected == buffer.ToBigInteger(), $"{overlap} words {words} shift {shift} seed {seed}");
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ShiftLeft_WiderResultOneBlockAboveTheOperand_GivesTheSeparateBufferResult()
    {
        // P1: ShiftLeft from words [0, n) into the n + 1 words [1024, n + 1025) of the same buffer (ShiftAndResize)
        // gives the shifted operand there, and the 1024 words below the result keep their value.
        // Coverage: the resize kernel strides by its 1024 threads, so without the copy each thread would read in its
        // second pass the word it wrote in its first: the failure does not depend on timing.
        // mutant: GpuBigIntMath.ShiftAndResizeCore `DeviceRanges.Overlaps(...)` -> `source.DevicePointer ==
        //         resultBuffer.DevicePointer`: fails
        const int Words = 2048;
        const int Gap = 1024;
        const int Shift = 7;
        const int Seed = 7011;
        var source0 = RandomValue(new Random(Seed), 32 * Words);

        using var buffer = GpuBigUInt.FromBigInteger(_workspace, source0, Gap + Words + 1);
        GpuBigUIntView view = buffer;
        GpuBigIntMath.ShiftLeft(view.Slice(0, Words), Shift, view.Slice(Gap, Words + 1));

        var below = source0 & ((BigInteger.One << (32 * Gap)) - 1);
        var expected = (source0 << Shift << (32 * Gap)) | below;
        Assert.True(expected == buffer.ToBigInteger(), $"seed {Seed}");
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(Operation.ShiftLeft)]
    [InlineData(Operation.ShiftRight)]
    public void Shift_InPlaceManyTimes_GivesTheShiftByTheSum(Operation operation)
    {
        // P1: 64 shifts in place by 5 bits of the largest operand that runs in one block (32 passes of 1024 threads,
        // each word reading its neighbour) give the shift by 320 bits.
        // Coverage: a missing barrier lets a warp write a word that another warp has not read yet; one shift does not
        // always show it, 64 make it near certain.
        // mutant: GpuBigIntKernels.ShiftLeftInPlaceKernel without `Gpu.SyncThreads()`: ShiftLeft fails
        // mutant: GpuBigIntKernels.ShiftRightInPlaceKernel without `Gpu.SyncThreads()`: ShiftRight fails
        const int Shift = 5;
        const int Count = 64;
        var seed = 7012 + (int)operation;
        // A left shift checks the bits shifted out, so its operand leaves the top 320 bits clear
        var bits = (32 * InPlaceMaxWords) - (operation == Operation.ShiftLeft ? Shift * Count : 0);
        var value0 = RandomValue(new Random(seed), bits);

        using var value = GpuBigUInt.FromBigInteger(_workspace, value0, InPlaceMaxWords);
        for (var n = 0; n < Count; n++)
        {
            if (operation == Operation.ShiftLeft)
            {
                GpuBigIntMath.ShiftLeft(value, Shift, value);
            }
            else
            {
                GpuBigIntMath.ShiftRight(value, Shift, value);
            }
        }

        var expected = operation == Operation.ShiftLeft ? value0 << (Shift * Count) : value0 >> (Shift * Count);
        Assert.True(expected == value.ToBigInteger(), $"{operation} seed {seed}");
    }

    /// <summary>
    /// The element-wise operations, the operand that shares words with the result (the left, or the right of a
    /// two-operand form), the word count, and how many words the result starts above (positive) or below the operand.
    /// </summary>
    public static TheoryData<Operation, bool, int, int> ElementwiseOverlapCases()
    {
        var data = new TheoryData<Operation, bool, int, int>();
        var sizes = new[] { (2048, 1), (2048, 1024), (5000, 2500) };
        foreach (var operation in new[]
                 {
                     Operation.Add, Operation.Sub, Operation.AddScalar, Operation.SubScalar, Operation.MulScalar,
                     Operation.BitwiseAnd, Operation.BitwiseOr, Operation.BitwiseXor, Operation.BitwiseNot,
                     Operation.DivideByUInt32
                 })
        {
            var twoOperands = operation is Operation.Add or Operation.Sub or Operation.BitwiseAnd or
                Operation.BitwiseOr or Operation.BitwiseXor;
            foreach (var (words, offset) in sizes)
            {
                foreach (var shared in twoOperands ? new[] { false, true } : new[] { false })
                {
                    data.Add(operation, shared, words, offset);
                    data.Add(operation, shared, words, -offset);
                }
            }
        }

        return data;
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ElementwiseOverlapCases))]
    public void Elementwise_ResultPartlyOverlapsAnOperand_GivesTheSeparateBufferResult(Operation operation,
        bool rightIsShared, int words, int offset)
    {
        // P1: the result words of the buffer hold the BigInteger result of the original operands, as with a separate
        // result buffer; the words of the buffer outside the result keep their value.
        // P2: the operand that does not share the buffer keeps its value.
        // Coverage: from 2048 words the single-block kernels of Add and Sub walk the words in passes of 1024, so a
        // result one word above the operand reads in its second pass a word the first pass wrote.
        // mutant: BigIntWideOps.PartlyOverlaps returns false (no temporary): 35 of the 90 cases fail, every Add, Sub
        //         and scalar case with the result above the operand, and some bitwise and DivideByUInt32 cases
        var seed = 7013 + ((int)operation * 1_000_003) + (words * 31) + offset + (rightIsShared ? 7 : 0);
        var random = new Random(seed);
        var (a0, b0) = Operands(operation, words, 0, random);
        const uint Scalar = 0x9E3779B9u;
        var gap = Math.Abs(offset);
        var filler0 = RandomValue(random, 32 * gap);
        var shared0 = rightIsShared ? b0 : a0;

        // The shared operand at words [0, n) with the filler above, or at [gap, n + gap) with the filler below; the
        // result at the other end
        var buffer0 = offset > 0 ? (filler0 << (32 * words)) | shared0 : (shared0 << (32 * gap)) | filler0;
        using var buffer = GpuBigUInt.FromBigInteger(_workspace, buffer0, words + gap);
        GpuBigUIntView view = buffer;
        var operandView = view.Slice(offset > 0 ? 0 : gap, words);
        var resultView = view.Slice(offset > 0 ? gap : 0, words);
        using var other = GpuBigUInt.FromBigInteger(_workspace, rightIsShared ? a0 : b0, words);
        var operand = operandView.AsBorrowedBigUInt();
        var (a, b) = rightIsShared ? (other, operand) : (operand, other);

        var value = Run(operation, a, b, resultView.AsBorrowedBigUInt(), 0, Scalar, a0, b0, words);

        var below = offset > 0 ? buffer0 & ((BigInteger.One << (32 * gap)) - 1) : BigInteger.Zero;
        var above = offset > 0 ? BigInteger.Zero : buffer0 >> (32 * words);
        var expected = offset > 0 ? (value << (32 * gap)) | below : (above << (32 * words)) | value;
        var message = $"{operation} words {words} offset {offset} shared {(rightIsShared ? "right" : "left")}, " +
                      $"seed {seed}";
        Assert.True(expected == buffer.ToBigInteger(), $"buffer: {message}");
        Assert.True((rightIsShared ? a0 : b0) == other.ToBigInteger(), $"other operand changed: {message}");
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(SmallWords, 37)]
    [InlineData(SmallWords, (32 * 300) + 7)]
    [InlineData(InPlaceMaxWords, (32 * 4096) + 5)]
    [InlineData(LargeWords, (32 * 4096) + 5)]
    public void ProviderShiftLeft_InPlaceWithHighBitsSet_DropsTheBitsShiftedOut(int words, int shift)
    {
        // P1: the provider's left shift (no overflow check) of a value with its top words set gives the shifted value
        // modulo 2^(32 n), in place as with a separate buffer.
        // Coverage: the public ShiftLeft rejects a set bit shifted out, so only the provider runs the in-place kernel
        // on words that leave the top.
        var seed = 7014 + (words * 31) + shift;
        var value0 = RandomValue(new Random(seed), 32 * words);
        using var value = GpuBigUInt.FromBigInteger(_workspace, value0, words);

        _workspace.Provider.ShiftLeft(_workspace, value, shift, value);

        var mask = (BigInteger.One << (32 * words)) - 1;
        Assert.True(((value0 << shift) & mask) == value.ToBigInteger(), $"words {words} shift {shift} seed {seed}");
    }

    // Runs the operation into result and returns the expected value
    private static BigInteger Run(Operation operation, GpuBigUInt a, GpuBigUInt b, GpuBigUInt result, int shift,
        uint scalar, BigInteger a0, BigInteger b0, int words)
    {
        var mask = (BigInteger.One << (32 * words)) - 1;
        switch (operation)
        {
            case Operation.ShiftLeft:
                GpuBigIntMath.ShiftLeft(a, shift, result);
                return a0 << shift;
            case Operation.ShiftRight:
                GpuBigIntMath.ShiftRight(a, shift, result);
                return a0 >> shift;
            case Operation.Add:
                GpuBigIntMath.Add(a, b, result);
                return a0 + b0;
            case Operation.AddScalar:
                GpuBigIntMath.Add(a, scalar, result);
                return a0 + scalar;
            case Operation.Sub:
                GpuBigIntMath.Sub(a, b, result);
                return a0 - b0;
            case Operation.SubScalar:
                GpuBigIntMath.Sub(a, scalar, result);
                return a0 - scalar;
            case Operation.MulScalar:
                GpuBigIntMath.Mul(a, scalar, result);
                return a0 * scalar;
            case Operation.BitwiseAnd:
                GpuBigIntMath.BitwiseAnd(a, b, result);
                return a0 & b0;
            case Operation.BitwiseOr:
                GpuBigIntMath.BitwiseOr(a, b, result);
                return a0 | b0;
            case Operation.BitwiseXor:
                GpuBigIntMath.BitwiseXor(a, b, result);
                return a0 ^ b0;
            case Operation.BitwiseNot:
                GpuBigIntMath.BitwiseNot(a, result);
                return mask ^ a0;
            default:
                var remainder = GpuBigIntMath.DivideByUInt32(a, scalar, result);
                Assert.Equal((uint)(a0 % scalar), remainder);
                return a0 / scalar;
        }
    }

    // Operands whose result fits in the operand width: a left shift keeps the top bits clear, a sum leaves the top bit
    // free, a difference has a larger minuend, a scalar product leaves the top word free
    private static (BigInteger A, BigInteger B) Operands(Operation operation, int words, int shift, Random random)
    {
        var bits = 32 * words;
        var aBits = operation switch
        {
            Operation.ShiftLeft => bits - shift,
            Operation.Add or Operation.AddScalar => bits - 1,
            Operation.MulScalar => bits - 32,
            _ => bits
        };
        var bBits = operation is Operation.Sub ? bits - 1 : operation is Operation.Add ? bits - 1 : bits;
        return (RandomValue(random, aBits), RandomValue(random, bBits));
    }

    // A value of exactly the given bit length (its top bit set), or zero for no bits
    private static BigInteger RandomValue(Random random, int bits)
    {
        if (bits <= 0)
        {
            return BigInteger.Zero;
        }

        var bytes = new byte[(bits + 7) / 8];
        random.NextBytes(bytes);
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
        value &= (BigInteger.One << bits) - 1;
        return value | (BigInteger.One << (bits - 1));
    }
}
