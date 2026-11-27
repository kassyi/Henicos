namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

/// <summary>
/// Encapsulates the host-orchestrated square-and-multiply algorithm loop for modular exponentiation.
/// Uses zero-sync Barrett reduction to execute entirely asynchronously without host-device synchronization.
/// </summary>
internal static class ModPowZeroSyncController
{
    // Interleaved sweep comparing CUDA Graph replay against the plain ladder loop across 1056 (33 words), 2048, and
    // 8192 bits (#2346): 2..4 steps regress at 1056 bits (0.81x..0.95x ModPow), while >= 5 steps are faster at every
    // measured width (1.01x..1.09x at 1056 bits, 1.10x..1.14x at 2048 bits, 1.22x..1.24x at 8192 bits).
    internal const int MinimumGraphSteps = 5;

    public static void Execute(GpuBigUInt baseVal, GpuBigUInt exp, GpuBigUInt mod, GpuBigUInt resultBuffer)
    {
        var expWords = exp.NumWords;
        using var buffers = PrepareLadder(baseVal, mod);
        var hostExp = ArrayPool<uint>.Shared.Rent(expWords);
        try
        {
            exp.Workspace.Provider.CopyDeviceToHost(hostExp, exp.DevicePointer, expWords, exp.Workspace.Stream);
            ExecuteLadder(buffers, hostExp, expWords);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostExp);
        }

        resultBuffer.CopyFrom(buffers.TempResult, numWords: buffers.NumWords);
    }

    internal static LadderBuffers PrepareLadder(GpuBigUInt baseVal, GpuBigUInt mod)
    {
        var workspace = baseVal.Workspace;
        var n = baseVal.NumWords;
        var modActualBits = GpuBigIntDivRem.GetActualBits(mod);
        if (modActualBits == 0)
        {
            throw new DivideByZeroException();
        }

        const int MarginBits = 512;
        var aActualWords = 4 * n;
        var targetBits = n * 32 + aActualWords * 32 + MarginBits;

        GpuBigUInt tempResult = default;
        GpuBigUInt tempBase = default;
        GpuBigUInt reciprocalOfMod = default;
        GpuBigUInt tempMul = default;
        GpuBigUInt tempQ = default;
        GpuBigUInt tempR = default;
        try
        {
            tempResult = new GpuBigUInt(workspace, 2 * n);
            // tempResult = 1 mod mod on the device (0 for modulus 1) so exponent 0 returns a reduced value.
            GpuBigIntDivRemKernels.LaunchInitializeOneModuloKernelPtr(
                tempResult.ToProxy(), mod.ToProxy(), n,
                gridDimX: 1, blockDimX: 1, hStream: workspace.Stream);

            reciprocalOfMod = ComputeModulusReciprocal(workspace, mod, n, modActualBits, targetBits, MarginBits / 32);
            tempBase = ReduceInitialBase(workspace, baseVal, mod, reciprocalOfMod, targetBits, aActualWords, n);
            tempMul = new GpuBigUInt(workspace, 4 * n);
            tempQ = new GpuBigUInt(workspace, 4 * n);
            tempR = new GpuBigUInt(workspace, 4 * n);
            return new LadderBuffers(
                workspace, tempResult, tempBase, mod, reciprocalOfMod,
                targetBits, aActualWords, tempMul, tempQ, tempR, n);
        }
        catch
        {
            tempR.Dispose();
            tempQ.Dispose();
            tempMul.Dispose();
            tempBase.Dispose();
            reciprocalOfMod.Dispose();
            tempResult.Dispose();
            throw;
        }
    }

    internal static void ExecuteLadder(LadderBuffers buffers, uint[] hostExp, int expWords)
    {
        var steps = CountLadderSteps(hostExp, expWords);
        if (steps < MinimumGraphSteps ||
            CaptureChecks.IsCapturing(buffers.Workspace) ||
            GpuKernelLoader.IsCpuFallbackMode)
        {
            var plain = new PlainStep(buffers, skipFirstStep: false);
            WalkLadder(hostExp, expWords, ref plain);
            return;
        }

        // Run the first step eagerly; the replay (or fallback) loop skips it.
        if ((hostExp[0] & 1u) == 1u)
        {
            buffers.Multiply();
        }
        else
        {
            buffers.Square();
        }

        if (!TryCaptureStepGraphs(buffers, out var multiplyGraph, out var squareGraph))
        {
            // Another thread holds this workspace's capture session; finish the ladder without graphs.
            var fallback = new PlainStep(buffers, skipFirstStep: true);
            WalkLadder(hostExp, expWords, ref fallback);
            return;
        }

        using (multiplyGraph)
        using (squareGraph)
        {
            var replay = new GraphReplayStep(buffers.Workspace, multiplyGraph, squareGraph);
            WalkLadder(hostExp, expWords, ref replay);
        }
    }

    internal static int CountLadderSteps(uint[] hostExp, int expWords)
    {
        CountStep counter = default;
        WalkLadder(hostExp, expWords, ref counter);
        return counter.Steps;
    }

    private static GpuBigUInt ComputeModulusReciprocal(
        GpuWorkspace workspace, GpuBigUInt mod, int n, int modActualBits, int targetBits, int marginWords)
    {
        var lz = n * 32 - modActualBits;
        using var bNorm = new GpuBigUInt(workspace, n);
        // lz is the count of leading zero bits of the modulus in its n words: only zero bits are shifted out.
        GpuBigIntMath.ShiftLeftUnchecked(mod, lz, bNorm);

        var targetWords = (targetBits + 31) / 32 + 1;
        using var reciprocalNorm = new GpuBigUInt(workspace, targetWords);
        GpuBigIntDivRem.Reciprocal(bNorm, reciprocalNorm, targetBits, marginWords);

        var reciprocalOfMod = new GpuBigUInt(workspace, targetWords);
        try
        {
            // The reciprocal shifted by lz is at most 2^targetBits (as in DivRem), which the targetWords words (one
            // word to spare) hold.
            GpuBigIntMath.ShiftLeftUnchecked(reciprocalNorm, lz, reciprocalOfMod);
            return reciprocalOfMod;
        }
        catch
        {
            reciprocalOfMod.Dispose();
            throw;
        }
    }

    private static GpuBigUInt ReduceInitialBase(
        GpuWorkspace workspace, GpuBigUInt baseVal, GpuBigUInt mod, GpuBigUInt reciprocalOfMod,
        int targetBits, int aActualWords, int n)
    {
        var tempBase = new GpuBigUInt(workspace, 2 * n);
        try
        {
            tempBase.CopyFrom(baseVal, numWords: n);
            using var tempBaseQ = new GpuBigUInt(workspace, 4 * n);
            using var tempBaseR = new GpuBigUInt(workspace, 4 * n);
            using var extendedTempBase = new GpuBigUInt(workspace, 4 * n);
            extendedTempBase.CopyFrom(tempBase, numWords: 2 * n);
            GpuBigIntDivRem.ReduceZeroSync(
                extendedTempBase, mod, reciprocalOfMod, targetBits, aActualWords, tempBaseQ, tempBaseR);
            tempBase.CopyFrom(tempBaseR, numWords: 2 * n);
            return tempBase;
        }
        catch
        {
            tempBase.Dispose();
            throw;
        }
    }

    private static bool TryCaptureStepGraphs(
        LadderBuffers buffers,
        [NotNullWhen(true)] out CapturedStreamGraph? multiplyGraph,
        [NotNullWhen(true)] out CapturedStreamGraph? squareGraph)
    {
        if (!CapturedStreamGraph.TryCapture(buffers.Workspace, buffers.Multiply, out multiplyGraph))
        {
            squareGraph = null;
            return false;
        }

        try
        {
            if (CapturedStreamGraph.TryCapture(buffers.Workspace, buffers.Square, out squareGraph))
            {
                return true;
            }
        }
        catch
        {
            multiplyGraph.Dispose();
            throw;
        }

        multiplyGraph.Dispose();
        multiplyGraph = null;
        squareGraph = null;
        return false;
    }

    private static void WalkLadder<TStep>(uint[] hostExp, int expWords, ref TStep step)
        where TStep : struct, ILadderStep
    {
        for (var i = 0; i < expWords; i++)
        {
            var e = hostExp[i];
            if (e == 0 && IsRemainingAllZero(hostExp, i, expWords))
            {
                break;
            }

            for (var bit = 0; bit < 32; bit++)
            {
                if ((e & 1u) == 1u)
                {
                    step.OnMultiply();
                }

                step.OnSquare();
                e >>= 1;

                if (e == 0 && i == expWords - 1)
                {
                    break;
                }
            }
        }
    }

    private static bool IsRemainingAllZero(uint[] hostExp, int startIndex, int expWords)
    {
        for (var j = startIndex; j < expWords; j++)
        {
            if (hostExp[j] != 0)
            {
                return false;
            }
        }

        return true;
    }

    internal sealed class LadderBuffers(
        GpuWorkspace workspace,
        [TakesOwnership] GpuBigUInt tempResult,
        [TakesOwnership] GpuBigUInt tempBase,
        GpuBigUInt mod,
        [TakesOwnership] GpuBigUInt reciprocalOfMod,
        int targetBits,
        int aActualWords,
        [TakesOwnership] GpuBigUInt tempMul,
        [TakesOwnership] GpuBigUInt tempQ,
        [TakesOwnership] GpuBigUInt tempR,
        int numWords) : IDisposable
    {
        private readonly GpuBigUInt _tempResult = tempResult;
        private readonly GpuBigUInt _tempBase = tempBase;
        private readonly GpuBigUInt _reciprocalOfMod = reciprocalOfMod;
        private readonly GpuBigUInt _tempMul = tempMul;
        private readonly GpuBigUInt _tempQ = tempQ;
        private readonly GpuBigUInt _tempR = tempR;

        public GpuWorkspace Workspace => workspace;

        public GpuBigUInt TempResult => _tempResult;

        public int NumWords => numWords;

        public void Multiply()
        {
            GpuBigIntMath.Mul(_tempResult, _tempBase, _tempMul);
            GpuBigIntDivRem.ReduceZeroSync(_tempMul, mod, _reciprocalOfMod, targetBits, aActualWords, _tempQ, _tempR);
            _tempResult.CopyFrom(_tempR, numWords: 2 * numWords);
        }

        public void Square()
        {
            GpuBigIntMath.Mul(_tempBase, _tempBase, _tempMul);
            GpuBigIntDivRem.ReduceZeroSync(_tempMul, mod, _reciprocalOfMod, targetBits, aActualWords, _tempQ, _tempR);
            _tempBase.CopyFrom(_tempR, numWords: 2 * numWords);
        }

        public void Dispose()
        {
            _tempR.Dispose();
            _tempQ.Dispose();
            _tempMul.Dispose();
            _tempBase.Dispose();
            _reciprocalOfMod.Dispose();
            _tempResult.Dispose();
        }
    }

    /// <summary>Receives each multiply and square step visited by <see cref="WalkLadder{TStep}"/>.</summary>
    private interface ILadderStep
    {
        /// <summary>Handles a multiply step for a set exponent bit.</summary>
        void OnMultiply();

        /// <summary>Handles a squaring step for the current exponent bit.</summary>
        void OnSquare();
    }

    private struct CountStep : ILadderStep
    {
        public int Steps;

        public void OnMultiply() => Steps++;

        public void OnSquare() => Steps++;
    }

    private struct PlainStep(LadderBuffers buffers, bool skipFirstStep) : ILadderStep
    {
        private bool _skipFirstStep = skipFirstStep;

        public void OnMultiply()
        {
            if (ConsumeSkip())
            {
                return;
            }

            buffers.Multiply();
        }

        public void OnSquare()
        {
            if (ConsumeSkip())
            {
                return;
            }

            buffers.Square();
        }

        private bool ConsumeSkip()
        {
            if (!_skipFirstStep)
            {
                return false;
            }

            _skipFirstStep = false;
            return true;
        }
    }

    private struct GraphReplayStep(
        GpuWorkspace workspace,
        CapturedStreamGraph multiplyGraph,
        CapturedStreamGraph squareGraph) : ILadderStep
    {
        private bool _skipFirstStep = true;

        public void OnMultiply()
        {
            if (ConsumeSkip())
            {
                return;
            }

            multiplyGraph.Launch();
            workspace.CountModPowGraphLaunch();
        }

        public void OnSquare()
        {
            if (ConsumeSkip())
            {
                return;
            }

            squareGraph.Launch();
            workspace.CountModPowGraphLaunch();
        }

        private bool ConsumeSkip()
        {
            if (!_skipFirstStep)
            {
                return false;
            }

            _skipFirstStep = false;
            return true;
        }
    }
}
