using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;
using static Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms.FermatTestMath;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

/// <summary>
/// <see cref="KernelAdvancedMathHandler.Square"/> with a caller-owned <see cref="KernelAdvancedMathHandler.NttSquareScratch"/>,
/// and the squaring path of <see cref="KernelAdvancedMathHandler.Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)"/>
/// that now runs through it. Expected squares come from <see cref="BigInteger"/>.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class KernelAdvancedMathSquareTests
{
    // 1 / 3 words: the shortest transforms; 128 / 129: on both sides of the shared-row transform (m <= 512 coefficients);
    // 257 / 1000 / 4096: two-pass transforms, 4096 words with several carry chunks of 1024 words.
    public static TheoryData<int> OperandWords => [1, 3, 128, 129, 257, 1000, 4096];

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(OperandWords))]
    public void Square_OwnedScratch_MatchesBigIntegerAcrossReuses(int words)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(2 * words);
        var handler = GSharpKernelProvider.Instance.KernelAdvancedMath;
        using var scratch = KernelAdvancedMathHandler.NttSquareScratch.Allocate(workspace, handler, words);
        using var result = new GpuBigUInt(workspace, 2 * words);
        var random = new Random(647 * 10_000 + words);

        // Three squares through the same scratch: a random value, all-ones words (a carry through every word of the
        // product) and another random value, so state left in the scratch by one square would corrupt the next.
        // mutant: KernelAdvancedMathHandler.cs Square `LaunchMultiplyNttPointwiseSquareKernelPtr(...)` removed
        // mutant: KernelAdvancedMathHandler.cs Square `2 * operand.NumWords` -> `operand.NumWords` (high half not written)
        for (var round = 0; round < 3; round++)
        {
            var operandWords = round == 1 ? AllOnes(words) : RandomWords(random, words);
            using var operand = new GpuBigUInt(workspace, operandWords);

            handler.Square(workspace, operand, result, scratch);

            var expected = FromWords(operandWords);
            Assert.True(expected * expected == FromWords(result.ToHostWords(workspace)),
                $"words={words} round={round} seed={647 * 10_000 + words}");
        }
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(OperandWords))]
    public void Mul_SameOperand_StillSquaresThroughRentedScratch(int words)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(2 * words);
        var operandWords = RandomWords(new Random(648 * 10_000 + words), words);
        using var operand = new GpuBigUInt(workspace, operandWords);
        using var result = new GpuBigUInt(workspace, 2 * words);

        // GpuBigIntMath.Mul with one buffer as both operands takes the squaring path (behavior unchanged).
        // mutant: KernelAdvancedMathHandler.cs Mul `Square(workspace, leftOperand, resultBuffer, scratch);` removed (returns
        // without a product)
        GpuBigIntMath.Mul(operand, operand, result);

        var expected = FromWords(operandWords);
        Assert.Equal(expected * expected, FromWords(result.ToHostWords(workspace)));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Square_OperandOfAnotherLength_Throws()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(64);
        var handler = GSharpKernelProvider.Instance.KernelAdvancedMath;
        using var scratch = KernelAdvancedMathHandler.NttSquareScratch.Allocate(workspace, handler, 16);
        using var operand = new GpuBigUInt(workspace, 17);
        using var result = new GpuBigUInt(workspace, 34);

        // The transform length and carry chunks were sized for 16-word operands.
        var exception = Assert.Throws<ArgumentException>(() => handler.Square(workspace, operand, result, scratch));
        Assert.Equal("operand", exception.ParamName);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void NttSquareScratch_BeyondTheTransformLimit_Throws()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(64);
        var handler = GSharpKernelProvider.Instance.KernelAdvancedMath;

        // 2^23 + 1 words square to more than 2^24 words, past the 2^25-coefficient transform; rejected before allocating.
        Assert.Throws<ArgumentException>(() =>
            KernelAdvancedMathHandler.NttSquareScratch.Allocate(workspace, handler, (1 << 23) + 1));
    }

    private static uint[] RandomWords(Random random, int words)
    {
        var bytes = new byte[words * 4];
        random.NextBytes(bytes);
        var result = new uint[words];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    private static uint[] AllOnes(int words)
    {
        var result = new uint[words];
        Array.Fill(result, uint.MaxValue);
        return result;
    }
}

internal static class GpuBigUIntTestExtensions
{
    /// <summary>Copies all words of a buffer to the host.</summary>
    public static uint[] ToHostWords(this GpuBigUInt value, GpuWorkspace workspace)
    {
        var words = new uint[value.NumWords];
        workspace.Provider.CopyDeviceToHost(words, value.DevicePointer, value.NumWords, workspace.Stream);
        return words;
    }
}
