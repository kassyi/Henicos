using System.Runtime.InteropServices;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;
using Kassyi.GSharp.Stdlib.GpuMath.Tests.Kernels;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class NttMultiplyMacroTests
{
    // Exercises the public ExecuteNttMultiplyMacro at both a single-row transform (M = 1024 <= 1024) and a
    // transpose-backed six-step transform (M = 131072 > 65536), comparing against BigInteger multiplication.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntDivRemKernels.cu.cs:38
    //   `ExecuteNttTransformOperand(core, opB, numWordsB, nttScratch.Kept1);` -> removed
    // input: (200, 200) and (20000, 20000)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(200, 200)]
    [InlineData(20000, 20000)]
    public void ExecuteNttMultiplyMacro_SingleRowAndTransposeLengths_MatchesBigIntegerProduct(
        int numWordsA,
        int numWordsB)
    {
        CudaCapability.EnsureGpuAvailable();

        const uint BlockDim = 512u;
        var numWordsResult = numWordsA + numWordsB;
        var seed = 2393 + numWordsA;
        var wordsA = CreateRandomWords(seed, numWordsA);
        var wordsB = CreateRandomWords(seed + 17, numWordsB);
        var expected = FromWords(wordsA) * FromWords(wordsB);

        var handler = new KernelAdvancedMathHandler();
        using var workspace = new GpuWorkspace(Math.Max(4096, numWordsResult * 4), GpuExecutionEngine.GSharpAot);
        using var gpuA = new GpuBigUInt(workspace, wordsA);
        using var gpuB = new GpuBigUInt(workspace, wordsB);
        using var gpuResult = new GpuBigUInt(workspace, numWordsResult);
        using var buffers = new CooperativeMegaKernelLauncher.NttWorkspaceBuffers(
            workspace, numWordsResult, numWordsResult, (int)BlockDim, handler);

        var sharedMemBytes = CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(buffers.TransformLength);
        var core = BuildMacroContext(gpuA, gpuResult, in buffers);
        using (new GpuKernelLoader.CudaContextScope(workspace.IsolatedContext.Handle))
        {
            NttMultiplyMacroTestKernels.LaunchMultiplyByNttMacroKernelPtr(
                core,
                gpuA.ToProxy(),
                gpuB.ToProxy(),
                gpuResult.ToProxy(),
                numWordsA,
                numWordsB,
                numWordsResult,
                gridDimX: 1,
                blockDimX: BlockDim,
                sharedMemBytes: sharedMemBytes,
                hStream: workspace.Stream);
        }

        var actual = FromWords(gpuResult.ToHostArray());
        Assert.True(expected == actual, $"seed {seed} mismatch for ({numWordsA}, {numWordsB})");
    }

    private static NttMultiplyMacroTestKernels.AotContexts.NewtonRaphson BuildMacroContext(
        GpuBigUInt operandA,
        GpuBigUInt result,
        in CooperativeMegaKernelLauncher.NttWorkspaceBuffers buffers) => new()
    {
        Operands = new NttMultiplyMacroTestKernels.AotContexts.Operand
        {
            TargetOperand = operandA.ToProxy(),
            ResultBuffer = result.ToProxy()
        },
        Ntt = new NttMultiplyMacroTestKernels.AotContexts.Ntt
        {
            TransformLength = buffers.TransformLength,
            TwiddleFactors = new NttMultiplyMacroTestKernels.AotContexts.TwiddleFactor
            {
                Forward1 = buffers.Twiddle.Forward1,
                Forward2 = buffers.Twiddle.Forward2,
                Inverse1 = buffers.Twiddle.Inverse1,
                Inverse2 = buffers.Twiddle.Inverse2
            }
        },
        Workspace = new NttMultiplyMacroTestKernels.AotContexts.WorkspaceState
        {
            Iteration = new NttMultiplyMacroTestKernels.AotContexts.IterationScratch
            {
                Diff = result.ToProxy()
            },
            NttScratch = new NttMultiplyMacroTestKernels.AotContexts.NttConvolutionScratch
            {
                Product = new NttMultiplyMacroTestKernels.AotContexts.NttResidues
                {
                    Buffer1 = buffers.ProductBuf1.ToProxy(),
                    Buffer2 = buffers.ProductBuf2.ToProxy()
                },
                Kept1 = new NttMultiplyMacroTestKernels.AotContexts.NttResidues
                {
                    Buffer1 = buffers.Kept1Buf1.ToProxy(),
                    Buffer2 = buffers.Kept1Buf2.ToProxy()
                },
                Kept2 = new NttMultiplyMacroTestKernels.AotContexts.NttResidues
                {
                    Buffer1 = buffers.Kept2Buf1.ToProxy(),
                    Buffer2 = buffers.Kept2Buf2.ToProxy()
                },
                TmpBuf = buffers.TmpBuf.ToProxy(),
                UncarriedLow = buffers.UncarriedLow.ToProxy(),
                UncarriedHigh = buffers.UncarriedHigh.ToProxy(),
                BlockCarries = buffers.BlockCarries.ToProxy()
            }
        }
    };

    private static uint[] CreateRandomWords(int seed, int count)
    {
        var rng = new Random(seed);
        var words = new uint[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = (uint)rng.Next() | ((uint)rng.Next(0, 2) << 31);
        }

        words[count - 1] |= 0x80000000u;
        return words;
    }

    private static BigInteger FromWords(ReadOnlySpan<uint> words) =>
        new(MemoryMarshal.AsBytes(words), isUnsigned: true, isBigEndian: false);
}
