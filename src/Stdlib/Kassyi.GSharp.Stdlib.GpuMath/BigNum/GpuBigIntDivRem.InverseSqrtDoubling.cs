namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Precision-doubling inverse square root driven from the host, for SqrtFloor roots wider than
// GpuWorkspace.SqrtPrecisionDoublingThresholdWords: a chain of ordinary launches whose working precision doubles from
// two words to the full width, with no host synchronization between the stages. The derivation and its error bounds
// are in docs/spec/en/02_architecture/04_bignum_internals.md.
public static partial class GpuBigIntDivRem
{
    /// <summary>
    /// The capacity of the stage buffer <see cref="InverseSqrtDoublingSchedule"/> writes into: 40, above the 33 stages
    /// of a final width of <see cref="int.MaxValue"/> words, the longest schedule.
    /// </summary>
    internal const int InverseSqrtDoublingMaxStages = 40;

    /// <summary>
    /// One stage of the precision-doubling inverse square root, as <see cref="InverseSqrtDoublingStagesForTest"/> reads
    /// it back.
    /// </summary>
    /// <param name="Words">The working words <c>p</c> of the stage.</param>
    /// <param name="Y">The result <c>Y</c> of the stage: <c>x / 2^E</c> as a fixed-point number with <c>32p</c>
    /// fraction bits, <c>E = 16w + 32m + 2</c>.</param>
    /// <param name="D">The scaled residual the stage used, signed:
    /// <c>D = floor((2^(64p + 60) - A_k·S) / 2^(32p + 60))</c>.</param>
    internal readonly record struct InverseSqrtDoublingStage(int Words, BigInteger Y, BigInteger D);

    /// <summary>
    /// Writes the working words <c>p_k</c> of the precision-doubling stages, first stage first:
    /// <c>p_K = finalWords</c>, <c>p_(k-1) = ceil(p_k / 2) + 1</c> (2 before a stage of 3 words), down to a first stage
    /// of 2 words.
    /// </summary>
    /// <param name="finalWords">The working words <c>p_K</c> of the last stage.</param>
    /// <param name="stageWords">Receives the working words of each stage.</param>
    /// <returns>The number of stages written.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="finalWords"/> is below 3.</exception>
    /// <exception cref="ArgumentException"><paramref name="stageWords"/> is shorter than the schedule.</exception>
    internal static int InverseSqrtDoublingSchedule(int finalWords, Span<int> stageWords)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(finalWords, 3);
        var count = 0;
        var words = finalWords;
        while (true)
        {
            if (count == stageWords.Length)
            {
                throw new ArgumentException(
                    $"The schedule of {finalWords} words has more than {stageWords.Length} stages.",
                    nameof(stageWords));
            }

            stageWords[count++] = words;
            if (words == 2)
            {
                break;
            }

            // ceil(p / 2) + 1 without overflow; a stage of 3 words follows one of 2
            var next = words - words / 2 + 1;
            words = next < words ? next : words - 1;
        }

        stageWords[..count].Reverse();
        return count;
    }

    /// <summary>
    /// Test only: runs the stages of the precision doubling up to <paramref name="finalWords"/> working words from
    /// <paramref name="firstY"/> and returns each stage, reading every stage back to the host.
    /// </summary>
    /// <param name="targetOperand">The operand <c>A</c> of <c>w</c> words, bit length <c>32w</c> or
    /// <c>32w - 1</c>.</param>
    /// <param name="finalWords">The working words <c>p_K</c> of the last stage, at least 3.</param>
    /// <param name="firstY">The input <c>Y</c> of the first stage, in <c>[0, 2^64)</c>.</param>
    /// <returns>The stages in order, each with its result <c>Y</c> and its residual <c>D</c>.</returns>
    /// <exception cref="ArgumentException">The operand has fewer than two words.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="finalWords"/> is below 3, or
    /// <paramref name="firstY"/> is negative or wider than 64 bits.</exception>
    internal static List<InverseSqrtDoublingStage> InverseSqrtDoublingStagesForTest(GpuBigUInt targetOperand,
        int finalWords, BigInteger firstY)
    {
        if (targetOperand.NumWords < 2)
        {
            throw new ArgumentException(
                "The precision doubling needs an operand of at least 2 words. "
                + $"operand: {targetOperand.NumWords} words.");
        }

        if (firstY.Sign < 0 || firstY.GetBitLength() > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(firstY), firstY, "The first Y must lie in [0, 2^64).");
        }

        using var y = targetOperand.Workspace.RentTemp(finalWords);
        WriteInverseSqrtDoublingFirstY(y, firstY);
        var trace = new List<InverseSqrtDoublingStage>();
        RunInverseSqrtDoublingStages(targetOperand, y, trace);
        return trace;
    }

    // The input Y of the first stage, below 2^64, into the top two words of y
    private static void WriteInverseSqrtDoublingFirstY(GpuBigUInt y, BigInteger firstY)
    {
        var workspace = y.Workspace;
        var firstStage = y.BorrowWords(y.NumWords - 2, 2);
        workspace.Provider.WriteWord(firstStage.DevicePointer, 0, (uint)(firstY & uint.MaxValue), workspace.Stream);
        workspace.Provider.WriteWord(firstStage.DevicePointer, 1, (uint)(firstY >> 32), workspace.Stream);
    }

    // The stages of InverseSqrtDoublingSchedule(y.NumWords) on y, whose top two words hold the input of the first
    // stage and whose other words are zero; y receives Y_K of the last stage in all its words
    private static void RunInverseSqrtDoublingStages(GpuBigUInt operand, GpuBigUInt y,
        List<InverseSqrtDoublingStage>? trace)
    {
        var finalWords = y.NumWords;
        Span<int> schedule = stackalloc int[InverseSqrtDoublingMaxStages];
        var stageCount = InverseSqrtDoublingSchedule(finalWords, schedule);

        using var scratch = InverseSqrtDoublingScratch.Rent(operand.Workspace, finalWords, operand.NumWords);
        var buffers = scratch.WithY(y);
        foreach (var words in schedule[..stageCount])
        {
            RunInverseSqrtDoublingStage(operand, buffers, finalWords, words, trace);
        }
    }

    // One Newton step x + (x / 2)(1 - A x^2) at p words; the four floors of the derivation are marked
    private static void RunInverseSqrtDoublingStage(GpuBigUInt operand, in InverseSqrtDoublingBuffers buffers,
        int finalWords, int words, List<InverseSqrtDoublingStage>? trace)
    {
        var operandWords = operand.NumWords;
        var residualWords = InverseSqrtDoublingResidualWords(words); // dw, also the words of Y in the third product
        var aWords = Math.Min(words + 1, operandWords);              // A_k: the top p + 1 words of A, or all of A
        var padWords = words + 1 - aWords;                           // zero words below A when A_k is wider
        var residualShift = 32 * words + 60 - 32 * padWords;         // D = floor(DD / 2^(32p + 60)), less the pad
        var productWords = aWords + words + 1;

        var y = buffers.Y.BorrowWords(finalWords - words, words);

        // P1: Y^2 exactly; S = floor(Y^2 / 2^(32(p - 1))) is its top p + 1 words (first floor)
        var squared = ResultBuffers.Head(buffers.Squared, 2 * words);
        GpuBigIntMath.Mul(y, y, squared);
        var s = squared.BorrowWords(words - 1, words + 1);

        // P2: T = A_k·S exactly; A_k = floor(A / 2^(32(w - p - 1))) (second floor), its zero pad words left out
        var aTop = operand.BorrowWords(operandWords - aWords, aWords);
        var product = ResultBuffers.Head(buffers.Product, productWords);
        GpuBigIntMath.Mul(aTop, s, product);

        // DD = 2^(64p + 60) - T is signed. Adding 2^(32 dw - 1) at the scale of D makes D + 2^(32 dw - 1)
        // non-negative for every |D| < 2^(32 dw - 1); the difference is taken modulo the width.
        var residual = ResultBuffers.Head(buffers.Residual, productWords);
        WriteTwoBitConstant(residual, 64 * words + 60 - 32 * padWords, residualShift + 32 * residualWords - 1);
        BigIntWideOps.AddSubtractModulo(residual, product, residual, subtract: true);
#if DEBUG
        // Debug builds check the bound |D| < 2^(32 dw - 1) that the derivation proves; outside it, the next read
        // throws OverflowException
        BigIntWideOps.CheckFitsBits(residual, residualShift + 32L * residualWords);
#endif
        // D + 2^(32 dw - 1) (third floor)
        var offsetResidual = ResultBuffers.Head(buffers.OffsetResidual, residualWords);
        GpuBigIntMath.ShiftAndResize(residual, -residualShift, offsetResidual);

        // P3: Yt·D = Yt·(D + 2^(32 dw - 1)) - Yt·2^(32 dw - 1), Yt the top dw words of Y, modulo
        // 2^(32(p + dw + 1)), which keeps the p words of dY = floor(Yt·D / 2^(32 dw + 1)) exact (fourth floor)
        var yTop = buffers.Y.BorrowWords(finalWords - residualWords, residualWords);
        var correction = ResultBuffers.Head(buffers.Correction, 2 * residualWords);
        GpuBigIntMath.Mul(yTop, offsetResidual, correction);
        var offset = ResultBuffers.Head(buffers.Offset, 2 * residualWords);
        GpuBigIntMath.ShiftAndResize(yTop, 32 * residualWords - 1, offset);
        var difference = ResultBuffers.Head(buffers.Difference, words + residualWords + 1);
        BigIntWideOps.AddSubtractModulo(correction, offset, difference, subtract: true);
        var step = ResultBuffers.Head(buffers.Step, words);
        GpuBigIntMath.ShiftAndResize(difference, -(32 * residualWords + 1), step);

        // Y' = Y + dY in place: dY is signed and Y' fits in p words, so the sum modulo 2^(32p) is exact
        BigIntWideOps.AddSubtractModulo(y, step, y, subtract: false);

        trace?.Add(new InverseSqrtDoublingStage(words, y.ToBigInteger(),
            offsetResidual.ToBigInteger() - (BigInteger.One << (32 * residualWords - 1))));
    }

    // dw = max(2, ceil(p / 2)): the words of D, and of Y in the third product
    private static int InverseSqrtDoublingResidualWords(int words) => Math.Max(2, words - words / 2);

    // Clears buffer and sets the bits highBit and lowBit (one bit when they are equal) with stream-ordered memsets
    private static void WriteTwoBitConstant(GpuBigUInt buffer, int highBit, int lowBit)
    {
        buffer.Clear();
        var workspace = buffer.Workspace;
        var lowWord = lowBit / 32;
        var lowValue = 1u << (lowBit % 32);
        if (highBit / 32 == lowWord)
        {
            lowValue |= 1u << (highBit % 32);
        }
        else
        {
            workspace.Provider.WriteWord(buffer.DevicePointer, highBit / 32, 1u << (highBit % 32), workspace.Stream);
        }

        workspace.Provider.WriteWord(buffer.DevicePointer, lowWord, lowValue, workspace.Stream);
    }

    /// <summary>
    /// The scratch buffers of <see cref="RunInverseSqrtDoublingStages"/>, sized for the last stage; each stage uses
    /// their low words. Not owned: the caller disposes them.
    /// </summary>
    private readonly ref struct InverseSqrtDoublingBuffers(GpuBigUInt y, GpuBigUInt squared, GpuBigUInt product,
        GpuBigUInt residual, GpuBigUInt offsetResidual, GpuBigUInt correction, GpuBigUInt offset, GpuBigUInt difference,
        GpuBigUInt step)
    {
        /// <summary><c>Y</c>: the stage of <c>p</c> words in the top <c>p</c> words, zeros below.</summary>
        public GpuBigUInt Y { get; } = y;

        /// <summary><c>Y^2</c>.</summary>
        public GpuBigUInt Squared { get; } = squared;

        /// <summary><c>T' = A_k'·S</c>, <c>aw + p + 1</c> words.</summary>
        public GpuBigUInt Product { get; } = product;

        /// <summary><c>C' - T'</c>, the offset residual before its shift.</summary>
        public GpuBigUInt Residual { get; } = residual;

        /// <summary><c>D + 2^(32 dw - 1)</c>.</summary>
        public GpuBigUInt OffsetResidual { get; } = offsetResidual;

        /// <summary><c>U = Yt·(D + 2^(32 dw - 1))</c>.</summary>
        public GpuBigUInt Correction { get; } = correction;

        /// <summary><c>V = Yt·2^(32 dw - 1)</c>.</summary>
        public GpuBigUInt Offset { get; } = offset;

        /// <summary><c>W = U - V</c>, modulo its width.</summary>
        public GpuBigUInt Difference { get; } = difference;

        /// <summary><c>dY</c> modulo <c>2^(32p)</c>.</summary>
        public GpuBigUInt Step { get; } = step;
    }

    /// <summary>
    /// Owns the eight rented scratch buffers of <see cref="RunInverseSqrtDoublingStages"/> and returns them on
    /// disposal.
    /// </summary>
    private readonly ref struct InverseSqrtDoublingScratch(GpuBigUInt squared, GpuBigUInt product, GpuBigUInt residual,
        GpuBigUInt offsetResidual, GpuBigUInt correction, GpuBigUInt offset, GpuBigUInt difference, GpuBigUInt step)
    {
        public static InverseSqrtDoublingScratch Rent(GpuWorkspace workspace, int finalWords, int operandWords)
        {
            var maxResidualWords = InverseSqrtDoublingResidualWords(finalWords);
            // aw + p + 1 of the last stage, aw = min(p + 1, w): the widest A_k·S and its residual
            var maxProductWords = Math.Min(finalWords + 1, operandWords) + finalWords + 1;
            return new InverseSqrtDoublingScratch(
                workspace.RentTemp(2 * finalWords),
                workspace.RentTemp(maxProductWords),
                workspace.RentTemp(maxProductWords),
                workspace.RentTemp(maxResidualWords),
                workspace.RentTemp(2 * maxResidualWords),
                workspace.RentTemp(2 * maxResidualWords),
                workspace.RentTemp(finalWords + maxResidualWords + 1),
                workspace.RentTemp(finalWords));
        }

        public InverseSqrtDoublingBuffers WithY(GpuBigUInt y) =>
            new(y, squared, product, residual, offsetResidual, correction, offset, difference, step);

        public void Dispose()
        {
            step.Dispose();
            difference.Dispose();
            offset.Dispose();
            correction.Dispose();
            offsetResidual.Dispose();
            residual.Dispose();
            product.Dispose();
            squared.Dispose();
        }
    }
}
