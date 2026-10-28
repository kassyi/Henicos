namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

using static FermatTestMath;

/// <summary>
/// <c>GpuBigIntFermatKernels.QuadraticFoldReduceKernel</c> against its host reference
/// <see cref="QuadraticFoldModulus.FoldReference"/>: both compute the same integer <c>F(F(F(x)))</c>, so their words must be
/// identical, not only congruent.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class QuadraticFoldReduceKernelTests
{
    // (E, threads per block). 1024 threads is what FermatAlgorithm launches; E = 40000 gives 2501-word residues and a
    // 3751-word first pass, so every pass runs several chunks of one block and carries cross the chunks. 32 threads (one
    // warp) with E = 1000 makes each 32-word chunk a chunk boundary. The other exponents put m = 2E + 1 and E next to a
    // word boundary (E % 32 = 0 / 1 / 31).
    public static TheoryData<int, uint> Cases => new()
    {
        { 32, 1024 },
        { 33, 1024 },
        { 63, 1024 },
        { 64, 1024 },
        { 65, 1024 },
        { 96, 1024 },
        { 1000, 32 },
        { 1000, 1024 },
        { 40000, 1024 }
    };

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(Cases))]
    public void Kernel_MatchesFoldReference_ForTheLargestAndRandomStates(int e, uint threads)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        var modulus = new QuadraticFoldModulus(e);
        var m = modulus.ModulusBits;
        using var workspace = new GpuWorkspace(modulus.ProductWords);
        using var product = new GpuBigUInt(workspace, modulus.ProductWords);
        using var firstFold = new GpuBigUInt(workspace, modulus.FirstFoldWords);
        using var secondFold = new GpuBigUInt(workspace, modulus.SecondFoldWords);
        using var residue = new GpuBigUInt(workspace, modulus.Words);
        var random = new Random(532 * 100_000 + e);

        // The largest state 2^(m+1) - 1 (all three passes needed), then random states below 2^(m+1).
        var states = new List<BigInteger> { (BigInteger.One << (m + 1)) - 1 };
        for (var i = 0; i < 4; i++)
        {
            states.Add(RandomBelow(random, BigInteger.One << (m + 1)));
        }

        foreach (var u in states)
        {
            var productWords = ToWords(u * u, modulus.ProductWords);
            workspace.Provider.CopyHostToDevice(product.DevicePointer, productWords, modulus.ProductWords, workspace.Stream);

            GpuBigIntFermatKernels.LaunchQuadraticFoldReduceKernelPtr(
                new GpuBigIntFermatKernels.AotContexts.QuadraticFold
                {
                    Product = product.DevicePointer,
                    FirstFold = firstFold.DevicePointer,
                    SecondFold = secondFold.DevicePointer,
                    Residue = residue.DevicePointer,
                    ProductWords = modulus.ProductWords,
                    FirstFoldWords = modulus.FirstFoldWords,
                    SecondFoldWords = modulus.SecondFoldWords,
                    ResidueWords = modulus.Words,
                    ModulusBits = m,
                    Exponent = e,
                    ConstantTerm = QuadraticFoldModulus.ConstantTerm
                },
                gridDimX: 1, blockDimX: threads, hStream: workspace.Stream);

            var actual = new uint[modulus.Words];
            workspace.Provider.CopyDeviceToHost(actual, residue.DevicePointer, modulus.Words, workspace.Stream);
            var expected = new uint[modulus.Words];
            modulus.FoldReference(productWords, expected);

            // P-agree: the kernel and the reference give the same words (the same representative below 2^m + 2^(E+1)).
            // mutant: GpuBigIntFermatKernels.cu.cs GetQuotientWord `index < 0 ? 0u :` removed (word -1 of q reads the remainder)
            // mutant: GpuBigIntFermatKernels.cu.cs QuadraticFoldPass `runningCarry = nextRunningCarry` removed (carry lost between chunks)
            Assert.True(expected.AsSpan().SequenceEqual(actual),
                $"E={e} threads={threads} seed={532 * 100_000 + e} u={(u == states[0] ? "2^(m+1)-1" : "random")}");

            // The reference itself is congruent to u^2 (checked in full by QuadraticFoldModulusTests).
            var n = ModulusOf(e);
            Assert.Equal(u * u % n, FromWords(actual) % n);
        }
    }
}
