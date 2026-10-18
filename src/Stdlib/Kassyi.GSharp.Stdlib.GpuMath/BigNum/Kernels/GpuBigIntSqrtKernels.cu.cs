namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntSqrtKernels
{
    // Dummy kernel to force the generator to emit the DualNtt struct definition before we use it
    [GpuDeviceFunction]
    private static void ForceEmitDualNtt_Sqrt(DualNtt args)
    {
    }

    /// <summary>
    ///Newton-Raphson Mega Kernel (Inverse Sqrt)
    /// </summary>
    [GpuKernel(Cooperative = true)]
    public static void InverseSqrtNewtonRaphsonMegaKernel(SqrtNewtonRaphson ctx)
    {
        var core = ctx.Core;
        var dims = core.Dimensions;
        var ops = core.Operands;
        var iterScratch = core.Workspace.Iteration;
        var n = dims.NumberOfWords;

        // main loop
        for (int i = 0; i < dims.Iterations; i++)
        {
            // 1. xSquared = resultBuffer * resultBuffer
            GpuBigIntDivRemKernels.ExecuteNttMultiplyMacro(core, ops.ResultBuffer, ops.ResultBuffer, iterScratch.XSquared,
                n + dims.MarginWords, n + dims.MarginWords, 2 * n + 2 * dims.MarginWords, 256);

            // 2. axSquared = targetOperand * xSquared
            GpuBigIntDivRemKernels.ExecuteNttMultiplyMacro(core, ops.TargetOperand, iterScratch.XSquared, iterScratch.AxSquared, n,
                2 * n + 2 * dims.MarginWords, 3 * n + 2 * dims.MarginWords, 256);

            // 3. diff = constant3 - axSquared
            Gpu.SingleThread(() =>
            {
                var subCtx = new GpuBigIntContext
                {
                    Operands = new OperandContext { CarryBuffer = core.CarryBuffer },
                    NumberOfWords = 3 * n + 2 * dims.MarginWords
                };
                GpuBigIntKernels.ExecuteSub(ref subCtx, iterScratch.Diff, ops.Constant3,
                    iterScratch.AxSquared);
            });
            Gpu.SyncGrid();

            // 4. nextXUnshifted = resultBuffer * diff
            GpuBigIntDivRemKernels.ExecuteNttMultiplyMacro(core, ops.ResultBuffer, iterScratch.Diff, iterScratch.NextXUnshifted,
                n + dims.MarginWords, 3 * n + 2 * dims.MarginWords,
                4 * n + 3 * dims.MarginWords, 256);

            // 5. resultBuffer = nextXUnshifted >> (2 * n * 32 + 2 * marginBits + 1)
            var shiftTotal = 2 * n * 32 + 2 * dims.MarginBits + 1;
            var wordShift = shiftTotal / 32;
            var bitShift = shiftTotal % 32;
            var destWords = n + dims.MarginWords;
            var srcMaxWords = 4 * n + 3 * dims.MarginWords;

            Gpu.GridStrideLoop(destWords, idx =>
            {
                var srcIdx = idx + wordShift;
                // Ensure we don't read out of bounds. The nextXUnshifted buffer size is 4n+3m
                var w0 = (srcIdx < srcMaxWords) ? iterScratch.NextXUnshifted[srcIdx] : 0u;
                var w1 = (srcIdx + 1 < srcMaxWords) ? iterScratch.NextXUnshifted[srcIdx + 1] : 0u;

                var shiftedValue = (w0 >> bitShift) | (bitShift == 0 ? 0u : (w1 << (32 - bitShift)));
                ops.ResultBuffer[idx] = shiftedValue;
            });
            Gpu.SyncGrid();
        }
    }
}
