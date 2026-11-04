namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

/// <summary>
/// Launches the 3-prime 35-bit tableless NTT multiplication, fused middle-pass squaring, and division-free Garner CRT3
/// carry resolution on the GPU.
/// </summary>
internal static class Ntt35ConvolutionLauncher
{
    // ResolveNtt35CarriesBlockedKernel is launched with blocks of this many threads by Multiply and Square.
    internal const int CarryChunkSize = 1024;

    /// <summary>
    /// Multiplies <paramref name="leftOperand"/> by <paramref name="rightOperand"/> into <paramref name="resultBuffer"/> by
    /// 3-prime 35-bit NTT, using only the buffers of <paramref name="scratch"/>: the call allocates nothing.
    /// </summary>
    public static void Multiply(
        GpuWorkspace workspace,
        GpuBigUInt leftOperand,
        GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer,
        NttMultiplyScratch scratch)
    {
        var outWords = leftOperand.NumWords + rightOperand.NumWords;
        if (outWords != scratch.ProductWords)
        {
            throw new ArgumentException(
                $"The scratch holds {scratch.ProductWords}-word products; the factors have {leftOperand.NumWords} and {rightOperand.NumWords} words.",
                nameof(scratch));
        }

        var shape = NttLaunchShape.For(scratch.TransformLength);
        var m = shape.TransformLength;

        // 1. Coalesced unpack of both operands into 35-bit limbs reduced mod (P1, P2, P3).
        LaunchUnpack35Pair(workspace, leftOperand, scratch.OperandA, rightOperand, scratch.OperandB, shape);

        // 2. Tableless forward NTTs across the 3 primes.
        for (var i = 0; i < PrimeModulusSpec.All.Length; i++)
        {
            var spec = PrimeModulusSpec.All[i];
            LaunchTablelessNttHost(workspace, scratch.OperandA[i], scratch.TmpBuf, shape, spec, isInverse: false);
            LaunchTablelessNttHost(workspace, scratch.OperandB[i], scratch.TmpBuf, shape, spec, isInverse: false);
        }

        // 3. Pointwise multiply across all 3 primes.
        Ntt35CarryKernels.LaunchMultiplyNtt35PointwiseMultiplyKernelPtr(
            new Ntt35CarryKernels.AotContexts.Ntt35PointwiseMultiply
            {
                Buf1A = scratch.OperandA[0].DevicePointer,
                Buf2A = scratch.OperandA[1].DevicePointer,
                Buf3A = scratch.OperandA[2].DevicePointer,
                Buf1B = scratch.OperandB[0].DevicePointer,
                Buf2B = scratch.OperandB[1].DevicePointer,
                Buf3B = scratch.OperandB[2].DevicePointer,
                TransformLength = m
            },
            gridDimX: shape.GridDim, blockDimX: shape.BlockDim,
            hStream: workspace.Stream);

        // 4. Tableless inverse NTTs across the 3 primes.
        for (var i = 0; i < PrimeModulusSpec.All.Length; i++)
        {
            var spec = PrimeModulusSpec.All[i];
            LaunchTablelessNttHost(workspace, scratch.OperandA[i], scratch.TmpBuf, shape, spec, isInverse: true);
        }

        // 5. Coalesced Garner CRT3 + carry resolution.
        ResolveNtt35Carries(workspace, shape, scratch, outWords, resultBuffer);
    }

    /// <summary>
    /// Squares <paramref name="operand"/> into <paramref name="resultBuffer"/> by tableless 3-prime 35-bit NTT with
    /// fused shared-memory middle passes and coalesced Garner CRT3 carry resolution, allocating nothing.
    /// </summary>
    public static void Square(
        GpuWorkspace workspace,
        GpuBigUInt operand,
        GpuBigUInt resultBuffer,
        NttProductScratch scratch)
    {
        if (2 * operand.NumWords != scratch.ProductWords)
        {
            throw new ArgumentException(
                $"The scratch holds {scratch.ProductWords}-word products; the operand has {operand.NumWords} words.",
                nameof(operand));
        }

        var shape = NttLaunchShape.For(scratch.TransformLength);

        // 1. Coalesced unpack of single operand into 35-bit limbs reduced mod (P1, P2, P3).
        LaunchUnpack35(workspace, operand, scratch.OperandA, shape);

        // 2. Tableless fused forward NTT + pointwise square + inverse NTT across the 3 primes.
        for (var i = 0; i < PrimeModulusSpec.All.Length; i++)
        {
            LaunchFusedSquareNttHost(workspace, scratch.OperandA[i], scratch.TmpBuf, shape, PrimeModulusSpec.All[i]);
        }

        // 3. Coalesced Garner CRT3 + carry resolution.
        ResolveNtt35Carries(workspace, shape, scratch, 2 * operand.NumWords, resultBuffer);
    }

    /// <summary>
    /// The transform length for a product of <paramref name="requiredLen"/> 32-bit words packed into 35-bit limbs:
    /// the power of two (at least 2) that holds <c>ceil(32 * requiredLen / 35)</c> coefficients.
    /// </summary>
    /// <exception cref="ArgumentException">The product exceeds <c>2^24</c> words.</exception>
    internal static int TransformLengthFor35(int requiredLen)
    {
        const int MaxProductWords = 1 << 24;
        if (requiredLen > MaxProductWords)
        {
            throw new ArgumentException(
                $"NTT product length exceeds the maximum supported limit of 2^24 ({MaxProductWords} words, up to 2^23 words per operand) bounded by the 2-adicity limit (25) of the NTT primes. Required product words: {requiredLen}.",
                nameof(requiredLen));
        }

        var requiredCoeffs = (int)(((ulong)(uint)requiredLen * 32UL + 34UL) / 35UL);
        var m = 2;
        while (m < requiredCoeffs)
        {
            m *= 2;
        }

        return m;
    }

    private static Ntt35CarryKernels.AotContexts.Ntt35UnpackOperand BuildUnpackOperandContext(
        GpuBigUInt operand, GpuBigUInt[] buffers) =>
        new()
        {
            Source = operand.DevicePointer,
            NttBuf1 = buffers[0].DevicePointer,
            NttBuf2 = buffers[1].DevicePointer,
            NttBuf3 = buffers[2].DevicePointer,
            NumWords = operand.NumWords
        };

    private static void LaunchUnpack35(
        GpuWorkspace workspace,
        GpuBigUInt operand,
        GpuBigUInt[] buffers,
        NttLaunchShape shape)
    {
        var unpackSingleCtx = new Ntt35CarryKernels.AotContexts.Ntt35UnpackSingle
        {
            Operand = BuildUnpackOperandContext(operand, buffers),
            Grid = shape.CarryGrid
        };
        if (shape.TransformLength <= 512)
        {
            Ntt35CarryKernels.LaunchMultiplyNtt35UnpackSingleSmallKernelPtr(
                unpackSingleCtx, gridDimX: shape.GridDim, blockDimX: shape.BlockDim, hStream: workspace.Stream);
            return;
        }

        var totalTiles = (uint)((shape.N1 >> 5) * (shape.N2 >> 5));
        var maxTileBlocks = PersistentBlocksFor(workspace, blocksPerSm: 8);
        Ntt35CarryKernels.LaunchMultiplyNtt35UnpackSingleTiledKernelPtr(
            unpackSingleCtx, gridDimX: Math.Min(maxTileBlocks, totalTiles), blockDimX: 1024, hStream: workspace.Stream);
    }

    private static void LaunchUnpack35Pair(
        GpuWorkspace workspace,
        GpuBigUInt leftOperand,
        GpuBigUInt[] leftBuffers,
        GpuBigUInt rightOperand,
        GpuBigUInt[] rightBuffers,
        NttLaunchShape shape)
    {
        var unpackCtx = new Ntt35CarryKernels.AotContexts.Ntt35Unpack
        {
            OperandA = BuildUnpackOperandContext(leftOperand, leftBuffers),
            OperandB = BuildUnpackOperandContext(rightOperand, rightBuffers),
            Grid = shape.CarryGrid
        };
        if (shape.TransformLength <= 512)
        {
            Ntt35CarryKernels.LaunchMultiplyNtt35UnpackSmallKernelPtr(
                unpackCtx, gridDimX: shape.GridDim, blockDimX: shape.BlockDim, hStream: workspace.Stream);
            return;
        }

        var totalTiles = (uint)((shape.N1 >> 5) * (shape.N2 >> 5));
        var maxTileBlocks = PersistentBlocksFor(workspace, blocksPerSm: 8);
        Ntt35CarryKernels.LaunchMultiplyNtt35UnpackTiledKernelPtr(
            unpackCtx, gridDimX: Math.Min(maxTileBlocks, totalTiles), blockDimX: 1024, hStream: workspace.Stream);
    }

    private static void LaunchTablelessNttHost(
        GpuWorkspace workspace,
        GpuBigUInt buffer,
        GpuBigUInt tempBuffer,
        NttLaunchShape shape,
        PrimeModulusSpec spec,
        bool isInverse)
    {
        var (m, n1, n2) = (shape.TransformLength, shape.N1, shape.N2);
        if (m <= 512)
        {
            var blockDim = (uint)Math.Max(32, m / 2);
            var padLen = m + (m >> 5);
            Ntt35TablelessKernels.LaunchMultiplyNttTablelessSharedRowKernelPtr(
                new Ntt35TablelessKernels.AotContexts.NttTablelessRow
                {
                    InputBuffer = buffer.DevicePointer,
                    OutputBuffer = buffer.DevicePointer,
                    RowRoot = spec.RootForLength(m, isInverse),
                    RootM = 1u,
                    P = spec.Prime,
                    Pm = spec.BarrettMagic,
                    ApplyTwiddleAfter = false,
                    MaxBlocks = 1,
                    CurrentL = m
                },
                gridDimX: 1, blockDimX: blockDim,
                sharedMemBytes: (uint)((padLen + m) * 4),
                hStream: workspace.Stream);
            return;
        }

        var firstMaxBlocks = isInverse ? n1 : n2;
        var firstL = isInverse ? n2 : n1;
        var secondMaxBlocks = isInverse ? n2 : n1;
        var secondL = isInverse ? n1 : n2;
        var rootM = spec.RootForLength(m, isInverse);

        LaunchTablelessSharedRow(workspace, buffer, buffer, spec, firstMaxBlocks, firstL,
            isInverse, applyTwiddleAfter: true, rootM);
        KernelAdvancedMathHandler.LaunchTranspose(workspace, buffer.DevicePointer, tempBuffer.DevicePointer,
            width: firstL, height: firstMaxBlocks);
        LaunchTablelessSharedRow(workspace, tempBuffer, buffer, spec, secondMaxBlocks, secondL,
            isInverse, applyTwiddleAfter: false, 1u);
    }

    private static void LaunchTablelessSharedRow(
        GpuWorkspace workspace,
        GpuBigUInt input,
        GpuBigUInt output,
        PrimeModulusSpec spec,
        int maxBlocks,
        int currentL,
        bool isInverse,
        bool applyTwiddleAfter,
        uint rootM)
    {
        var threadsNeeded = currentL / 2;
        var blockDim = (uint)Math.Min(512, Math.Max(256, threadsNeeded));
        var persistentBlocks = PersistentBlocksFor(workspace, currentL <= 2048 ? 6u : 3u);
        var gridDim = Math.Min(persistentBlocks, (uint)maxBlocks);
        var padLen = currentL + (currentL >> 5);

        Ntt35TablelessKernels.LaunchMultiplyNttTablelessSharedRowKernelPtr(
            new Ntt35TablelessKernels.AotContexts.NttTablelessRow
            {
                InputBuffer = input.DevicePointer,
                OutputBuffer = output.DevicePointer,
                RowRoot = spec.RootForLength(currentL, isInverse),
                RootM = rootM,
                P = spec.Prime,
                Pm = spec.BarrettMagic,
                ApplyTwiddleAfter = applyTwiddleAfter,
                MaxBlocks = maxBlocks,
                CurrentL = currentL
            },
            gridDimX: gridDim, blockDimX: blockDim,
            sharedMemBytes: (uint)((padLen + currentL) * 4),
            hStream: workspace.Stream);
    }

    private static void LaunchFusedSquareNttHost(
        GpuWorkspace workspace,
        GpuBigUInt buffer,
        GpuBigUInt tempBuffer,
        NttLaunchShape shape,
        PrimeModulusSpec spec)
    {
        var (m, n1, n2) = (shape.TransformLength, shape.N1, shape.N2);
        if (m <= 512)
        {
            var blockDim = (uint)Math.Max(32, m / 2);
            var padLen = m + (m >> 5);
            Ntt35TablelessKernels.LaunchMultiplyNttFusedMiddleSquareRowKernelPtr(
                new Ntt35TablelessKernels.AotContexts.NttFusedMiddleSquare
                {
                    InputBuffer = buffer.DevicePointer,
                    OutputBuffer = buffer.DevicePointer,
                    RowForwardRoot = spec.RootForLength(m, isInverse: false),
                    InverseRootM = 1u,
                    P = spec.Prime,
                    Pm = spec.BarrettMagic,
                    ApplyTwiddleAfter = false,
                    MaxBlocks = 1,
                    CurrentL = m
                },
                gridDimX: 1, blockDimX: blockDim,
                sharedMemBytes: (uint)((padLen + m) * 4),
                hStream: workspace.Stream);
            return;
        }

        // Forward Pass 1: n2 rows of length n1 on buffer, with twiddle after.
        LaunchTablelessSharedRow(workspace, buffer, buffer, spec, maxBlocks: n2, currentL: n1,
            isInverse: false, applyTwiddleAfter: true, rootM: spec.RootForLength(m, isInverse: false));

        // Forward Transpose: (n2 x n1) buffer -> (n1 x n2) tempBuffer.
        KernelAdvancedMathHandler.LaunchTranspose(workspace, buffer.DevicePointer, tempBuffer.DevicePointer,
            width: n1, height: n2);

        // Fused Middle Pass: Forward Pass 2 + Pointwise Square + In-Shared Bit-Reverse + Inverse Pass 1 (with twiddle after).
        var threadsNeeded = n2 / 2;
        var midBlockDim = (uint)Math.Min(512, Math.Max(256, threadsNeeded));
        var persistentMidBlocks = PersistentBlocksFor(workspace, n2 <= 2048 ? 6u : 3u);
        var midGridDim = Math.Min(persistentMidBlocks, (uint)n1);
        var midPadLen = n2 + (n2 >> 5);
        Ntt35TablelessKernels.LaunchMultiplyNttFusedMiddleSquareRowKernelPtr(
            new Ntt35TablelessKernels.AotContexts.NttFusedMiddleSquare
            {
                InputBuffer = tempBuffer.DevicePointer,
                OutputBuffer = buffer.DevicePointer,
                RowForwardRoot = spec.RootForLength(n2, isInverse: false),
                InverseRootM = spec.RootForLength(m, isInverse: true),
                P = spec.Prime,
                Pm = spec.BarrettMagic,
                ApplyTwiddleAfter = true,
                MaxBlocks = n1,
                CurrentL = n2
            },
            gridDimX: midGridDim, blockDimX: midBlockDim,
            sharedMemBytes: (uint)((midPadLen + n2) * 4),
            hStream: workspace.Stream);

        // Inverse Transpose: (n1 x n2) buffer -> (n2 x n1) tempBuffer.
        KernelAdvancedMathHandler.LaunchTranspose(workspace, buffer.DevicePointer, tempBuffer.DevicePointer,
            width: n2, height: n1);

        // Inverse Pass 2: n2 rows of length n1 from tempBuffer into transposed buffer.
        LaunchTablelessSharedRow(workspace, tempBuffer, buffer, spec, maxBlocks: n2, currentL: n1,
            isInverse: true, applyTwiddleAfter: false, rootM: 1u);
    }

    private static void ResolveNtt35Carries(
        GpuWorkspace workspace,
        NttLaunchShape shape,
        NttProductScratch scratch,
        int outWords,
        GpuBigUInt resultBuffer)
    {
        var writeWords = Math.Min(outWords, resultBuffer.NumWords);
        var primes = PrimeModulusSpec.All;
        var ctx = new Ntt35CarryKernels.AotContexts.Ntt35ResolveCarries
        {
            Result1 = scratch.OperandA[0].DevicePointer,
            Result2 = scratch.OperandA[1].DevicePointer,
            Result3 = scratch.OperandA[2].DevicePointer,
            OutputBuffer = resultBuffer.DevicePointer,
            BlockCarries = scratch.BlockCarries.DevicePointer,
            Grid = shape.CarryGrid,
            MaxWordCount = writeWords,
            ChunkSize = CarryChunkSize,
            InvM1 = primes[0].InverseOfLength(shape.TransformLength),
            InvM2 = primes[1].InverseOfLength(shape.TransformLength),
            InvM3 = primes[2].InverseOfLength(shape.TransformLength)
        };

        if (shape.TransformLength <= 512)
        {
            KernelAdvancedMathHandler.ThrowIfBlockCarriesTooSmall(
                scratch.BlockCarries.NumWords, writeWords, CarryChunkSize);
            var numBlocks = (writeWords + CarryChunkSize - 1) / CarryChunkSize;
            Ntt35CarryKernels.LaunchResolveNtt35CarriesBlockedKernelPtr(
                ctx,
                gridDimX: (uint)Math.Min(65535, Math.Max(1, numBlocks)),
                blockDimX: CarryChunkSize,
                hStream: workspace.Stream);

            if (numBlocks > 1)
            {
                var applyBlockDim = (uint)Math.Min(1024, Math.Max(256, numBlocks));
                var applyGridDim = (uint)((numBlocks + applyBlockDim - 1) / applyBlockDim);
                GpuBigIntNttKernels.LaunchResolveNttCarriesApplyKernelPtr(
                    resultBuffer.DevicePointer, scratch.BlockCarries.DevicePointer, writeWords, CarryChunkSize,
                    numBlocks,
                    gridDimX: applyGridDim, blockDimX: applyBlockDim,
                    hStream: workspace.Stream);
            }

            return;
        }

        // Coalesced 32x32 tiled Garner CRT3 + 35-word tile carry resolution (zero extra transpose passes).
        const int TileOutputWords = 35;
        var numTiles = shape.TransformLength >> 5;
        if (scratch.BlockCarries.NumWords < numTiles)
        {
            throw new InvalidOperationException(
                $"The NTT block-carry buffer holds {scratch.BlockCarries.NumWords} words, but {numTiles} 35-word tiles require {numTiles} carries.");
        }

        var totalTileBlocks = (uint)((shape.N1 >> 5) * (shape.N2 >> 5));
        var maxTileBlocks = PersistentBlocksFor(workspace, blocksPerSm: 8);
        Ntt35CarryKernels.LaunchResolveNtt35CarriesTiled32x32KernelPtr(
            ctx,
            gridDimX: Math.Min(maxTileBlocks, totalTileBlocks),
            blockDimX: 1024,
            hStream: workspace.Stream);

        if (numTiles > 1)
        {
            var applyBlockDim = (uint)Math.Min(1024, Math.Max(256, numTiles));
            var applyGridDim = (uint)((numTiles + applyBlockDim - 1) / applyBlockDim);
            GpuBigIntNttKernels.LaunchResolveNttCarriesApplyKernelPtr(
                resultBuffer.DevicePointer, scratch.BlockCarries.DevicePointer, writeWords, TileOutputWords,
                numTiles,
                gridDimX: applyGridDim, blockDimX: applyBlockDim,
                hStream: workspace.Stream);
        }
    }

    internal static uint PersistentBlocksFor(GpuWorkspace workspace, uint blocksPerSm)
    {
        var smCount = (uint)Math.Max(1, workspace.DeviceSmCount);
        return smCount * blocksPerSm;
    }

    internal readonly record struct PrimeModulusSpec(uint Prime, uint PrimitiveRoot, ulong BarrettMagic)
    {
        public static readonly PrimeModulusSpec[] All =
        [
            new(NttPrimeArithmetic.Prime1, NttPrimeArithmetic.NttG1, NttPrimeArithmetic.BarrettMagic1),
            new(NttPrimeArithmetic.Prime2, NttPrimeArithmetic.NttG2, NttPrimeArithmetic.BarrettMagic2),
            new(NttPrimeArithmetic.Prime3, NttPrimeArithmetic.NttG3, NttPrimeArithmetic.BarrettMagic3)
        ];

        public uint RootForLength(int length, bool isInverse)
        {
            var forwardRoot = ModPowScalar(PrimitiveRoot, (Prime - 1u) / (uint)length, Prime);
            return isInverse ? ModPowScalar(forwardRoot, Prime - 2u, Prime) : forwardRoot;
        }

        public uint InverseOfLength(int length) => ModPowScalar((uint)length, Prime - 2u, Prime);

        public static uint ModPowScalar(ulong baseVal, ulong exp, uint p)
        {
            ulong res = 1;
            baseVal %= p;
            while (exp > 0)
            {
                if ((exp & 1) != 0)
                {
                    res = (res * baseVal) % p;
                }

                baseVal = (baseVal * baseVal) % p;
                exp >>= 1;
            }

            return (uint)res;
        }
    }

    /// <summary>The 2D split <c>m = N1 * N2</c> of a transform length and the launch size of the per-element kernels.</summary>
    internal readonly record struct NttLaunchShape(int TransformLength, int N1, int N2, uint GridDim, uint BlockDim)
    {
        public Ntt35CarryKernels.AotContexts.NttGrid CarryGrid => new() { M = TransformLength, N1 = N1, N2 = N2 };

        public static NttLaunchShape For(int m)
        {
            var n2 = 1;
            while (n2 * n2 < m)
            {
                n2 *= 2;
            }

            var n1 = m / n2;
            var maxL = m <= 512 ? m : Math.Max(n1, n2);
            var threadsNeeded = Math.Max(1, maxL / 2);

            var blockDim = (uint)Math.Min(1024, Math.Max(256, threadsNeeded));
            var gridDim = (uint)((m + blockDim - 1) / blockDim);
            return new NttLaunchShape(m, n1, n2, gridDim, blockDim);
        }
    }

    /// <summary>
    /// The 3-prime 35-bit NTT buffers of products of <see cref="ProductWords"/> words for <see cref="Square"/> or
    /// <see cref="Multiply"/>.
    /// </summary>
    internal abstract class NttProductScratch : IDisposable
    {
        private protected NttProductScratch(GpuWorkspace workspace, int productWords, bool secondOperand, bool rent)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productWords);
            ProductWords = productWords;
            TransformLength = TransformLengthFor35(productWords);
            NumBlocks = (productWords + CarryChunkSize - 1) / CarryChunkSize;
            var carryWords = Math.Max(NumBlocks * 2, Math.Max(2, TransformLength >> 5));

            OperandA = new GpuBigUInt[PrimeModulusSpec.All.Length];
            OperandB = secondOperand ? new GpuBigUInt[PrimeModulusSpec.All.Length] : [];
            try
            {
                for (var i = 0; i < OperandA.Length; i++)
                {
                    OperandA[i] = Take(workspace, TransformLength, rent);
                }

                for (var i = 0; i < OperandB.Length; i++)
                {
                    OperandB[i] = Take(workspace, TransformLength, rent);
                }

                TmpBuf = Take(workspace, TransformLength, rent);
                BlockCarries = Take(workspace, carryWords, rent);
            }
            catch
            {
                // Release what was taken before the failure; the default buffers not yet taken ignore Dispose.
                Dispose();
                throw;
            }
        }

        /// <summary>Gets the word count of the products this scratch computes.</summary>
        public int ProductWords { get; }

        /// <summary>Gets the NTT transform length (35-bit limbs) of a product.</summary>
        public int TransformLength { get; }

        /// <summary>Gets the number of carry chunks of a product.</summary>
        public int NumBlocks { get; }

        internal GpuBigUInt[] OperandA { get; }
        internal GpuBigUInt[] OperandB { get; }
        internal GpuBigUInt TmpBuf { get; }
        internal GpuBigUInt BlockCarries { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            for (var i = 0; i < OperandA.Length; i++)
            {
                OperandA[i].Dispose();
            }

            for (var i = 0; i < OperandB.Length; i++)
            {
                OperandB[i].Dispose();
            }

            TmpBuf.Dispose();
            BlockCarries.Dispose();
        }

        private static GpuBigUInt Take(GpuWorkspace workspace, int words, bool rent) =>
            rent ? workspace.RentTemp(words) : new GpuBigUInt(workspace, words);
    }

    /// <summary>The NTT buffers for squaring an <see cref="OperandWords"/>-word operand with <see cref="Square"/>.</summary>
    internal sealed class NttSquareScratch : NttProductScratch
    {
        private NttSquareScratch(GpuWorkspace workspace, int operandWords, bool rent)
            : base(workspace, 2 * operandWords, secondOperand: false, rent) =>
            OperandWords = operandWords;

        /// <summary>Gets the word count of the operands this scratch squares.</summary>
        public int OperandWords { get; }

        /// <summary>Creates a scratch whose buffers it owns until <see cref="NttProductScratch.Dispose"/>.</summary>
        public static NttSquareScratch Allocate(GpuWorkspace workspace, int operandWords) =>
            new(workspace, operandWords, rent: false);

        /// <summary>Creates a scratch from the workspace's temporary buffers, for a single square.</summary>
        public static NttSquareScratch Rent(GpuWorkspace workspace, int operandWords) =>
            new(workspace, operandWords, rent: true);
    }

    /// <summary>The NTT buffers for products of two operands with <see cref="Multiply"/> (and shared <see cref="Square"/>).</summary>
    internal sealed class NttMultiplyScratch : NttProductScratch
    {
        private NttMultiplyScratch(GpuWorkspace workspace, int productWords, bool rent)
            : base(workspace, productWords, secondOperand: true, rent)
        {
        }

        /// <summary>Creates a scratch whose buffers it owns until <see cref="NttProductScratch.Dispose"/>.</summary>
        public static NttMultiplyScratch Allocate(GpuWorkspace workspace, int productWords) =>
            new(workspace, productWords, rent: false);

        /// <summary>Creates a scratch from the workspace's temporary buffers, for a single product.</summary>
        public static NttMultiplyScratch Rent(GpuWorkspace workspace, int productWords) =>
            new(workspace, productWords, rent: true);
    }
}

