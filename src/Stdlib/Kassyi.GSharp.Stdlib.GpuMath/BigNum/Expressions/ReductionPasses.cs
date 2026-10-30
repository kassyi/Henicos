namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The passes of a chunked reduction, shared by the integer and the floating-point vector reductions.
/// </summary>
internal static class ReductionPasses
{
    /// <summary>
    /// Plans the passes over <paramref name="length"/> elements: pass i reduces up to <c>BatchSize</c> inputs into
    /// <c>GridDimX = ceil(BatchSize / chunkSize)</c> partial results, until one is left. One element still gets one pass;
    /// no element gets none.
    /// </summary>
    public static List<(int GridDimX, int BatchSize)> Plan(int length, int chunkSize)
    {
        var passes = new List<(int GridDimX, int BatchSize)>();
        var batchSize = length;
        if (batchSize <= 0)
        {
            return passes;
        }

        do
        {
            var nextBatchSize = (batchSize + chunkSize - 1) / chunkSize;
            passes.Add((GridDimX: nextBatchSize, BatchSize: batchSize));
            batchSize = nextBatchSize;
        }
        while (batchSize > 1);

        return passes;
    }
}
