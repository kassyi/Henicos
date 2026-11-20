namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// BorrowWords returns a view of a word range: it reads and writes the owner's words at the offset, a view of a view
// adds the offsets, disposing a view leaves the owner usable, and a range outside the owner throws.
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class GpuBigUIntBorrowWordsTests
{
    private static readonly uint[] s_words = [10, 11, 12, 13, 14, 15, 16, 17];

    // mutant: GpuBigUInt.cs:461 `Handle.Slice(wordOffset, numWords)` -> `Handle.Slice(0, numWords)` (the view reads the
    //         first words)
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void BorrowWords_ViewOfAView_ReadsAndWritesTheOwnerAtTheSummedOffset()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        using var owner = new GpuBigUInt(ctx, s_words);

        using (var view = owner.BorrowWords(2, 5))
        {
            Assert.Equal(new uint[] { 12, 13, 14, 15, 16 }, view.ToHostArray());
            using var inner = view.BorrowWords(1, 2);
            Assert.Equal(new uint[] { 13, 14 }, inner.ToHostArray());
            inner.Clear();
        }

        // The writes through the inner view landed at words 3 and 4, and disposing both views left the owner usable
        Assert.Equal(new uint[] { 10, 11, 12, 0, 0, 15, 16, 17 }, owner.ToHostArray());
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(-1, 2)]
    [InlineData(7, 2)]
    [InlineData(0, 9)]
    [InlineData(8, 1)]
    public void BorrowWords_RangeOutsideTheOwner_ThrowsArgumentOutOfRangeException(int wordOffset, int numWords)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        using var owner = new GpuBigUInt(ctx, s_words);

        Assert.Throws<ArgumentOutOfRangeException>(() => owner.BorrowWords(wordOffset, numWords));
    }
}
