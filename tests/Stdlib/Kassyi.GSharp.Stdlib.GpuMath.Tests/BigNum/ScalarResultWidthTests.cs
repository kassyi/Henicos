#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// Properties of GpuBigIntMath.Add/Sub/Mul(a, uint, r) against a BigInteger oracle:
// - a result buffer narrower than the operand is rejected before anything is written;
// - a wider buffer receives the exact result and zeros above it;
// - a buffer as wide as the operand (the checked form) holds the exact result when it fits, and a carry out, a non-zero
//   top word of the product or a difference below zero is a domain error: OverflowException at the next read on the
//   GPU, at the call on the CPU fallback; never a silently dropped word.
// Operand widths cover one word, a partial warp (33), one full block pass (1024, 1025) and several passes (2049), with
// carry and borrow chains across all of them.

/// <summary>The scalar operations of <see cref="GpuBigIntMath"/> on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class ScalarResultWidthTests : IDisposable
{
    private readonly GpuTestWorkspace _testWorkspace;

    public ScalarResultWidthTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _testWorkspace = new GpuTestWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
    }

    private GpuWorkspace Workspace => _testWorkspace.Workspace;

    public void Dispose() => _testWorkspace.Dispose();

    public static TheoryData<ScalarOperation> Operations() =>
        [ScalarOperation.Add, ScalarOperation.Sub, ScalarOperation.Mul];

    public static TheoryData<ScalarOperation, int> OperationsAndWidths()
    {
        var data = new TheoryData<ScalarOperation, int>();
        foreach (var operation in new[] { ScalarOperation.Add, ScalarOperation.Sub, ScalarOperation.Mul })
        {
            foreach (var words in ScalarCases.Widths)
            {
                data.Add(operation, words);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Operations))]
    [Scenario(TestScenario.SadPath)]
    public void NarrowerResult_IsRejected_AndNothingIsWritten(ScalarOperation operation)
    {
        using var a = new GpuBigUInt(Workspace, [5u, 6u, 7u, 8u]);
        using var buffer = new GpuBigUInt(Workspace, Enumerable.Repeat(ScalarCases.Fill, 8).ToArray());

        foreach (var resultWords in new[] { 1, 3 })
        {
            Assert.Throws<ArgumentException>(() =>
                ScalarCases.Apply(operation, a, 2u, ((GpuBigUIntView)buffer).Slice(0, resultWords)));
        }

        Assert.All(buffer.ToHostArray(), static w => Assert.Equal(ScalarCases.Fill, w));
        Workspace.ThrowIfDeviceErrors();
    }

    [Theory]
    [MemberData(nameof(OperationsAndWidths))]
    [Scenario(TestScenario.Boundary)]
    public void WiderResult_HoldsTheExactValue_AndZerosAbove(ScalarOperation operation, int words)
    {
        var (value, scalar) = ScalarCases.CarryChain(operation, words);
        using var a = GpuBigUInt.FromBigInteger(Workspace, value, words);

        foreach (var extra in new[] { 1, 3 })
        {
            using var result = new GpuBigUInt(Workspace, Enumerable.Repeat(ScalarCases.Fill, words + extra).ToArray());
            ScalarCases.Apply(operation, a, scalar, result);

            Assert.Equal(ScalarCases.Limbs(ScalarCases.Oracle(operation, value, scalar), words + extra),
                result.ToHostArray());
        }

        Workspace.ThrowIfDeviceErrors();
    }

    [Theory]
    [MemberData(nameof(OperationsAndWidths))]
    [Scenario(TestScenario.HappyPath)]
    public void EqualWidth_WithoutCarry_IsExact(ScalarOperation operation, int words)
    {
        var (value, scalar) = ScalarCases.NoCarry(operation, words);
        using var a = GpuBigUInt.FromBigInteger(Workspace, value, words);
        using var result = new GpuBigUInt(Workspace, Enumerable.Repeat(ScalarCases.Fill, words).ToArray());

        ScalarCases.Apply(operation, a, scalar, result);

        Assert.Equal(ScalarCases.Limbs(ScalarCases.Oracle(operation, value, scalar), words), result.ToHostArray());
        Workspace.ThrowIfDeviceErrors();
    }

    [Theory]
    [MemberData(nameof(OperationsAndWidths))]
    [Scenario(TestScenario.SadPath)]
    public void EqualWidth_WithCarry_IsReportedOnceAtTheNextRead(ScalarOperation operation, int words)
    {
        var (value, scalar) = ScalarCases.OutOfDomain(operation, words);
        using var a = GpuBigUInt.FromBigInteger(Workspace, value, words);
        using var result = new GpuBigUInt(Workspace, words);

        ScalarCases.Apply(operation, a, scalar, result);

        var error = Assert.Throws<OverflowException>(() => result.ToHostArray());
        Assert.Contains($"GpuBigIntMath.{operation}", error.Message);

        // Reported once: the next read and an explicit check pass
        result.ToHostArray();
        Workspace.ThrowIfDeviceErrors();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(33)]
    [InlineData(2049)]
    [Scenario(TestScenario.SadPath)]
    public void Subtraction_BelowZero_IsReported_AlsoIntoAWiderResult(int words)
    {
        using var a = GpuBigUInt.FromBigInteger(Workspace, 1, words);
        using var result = new GpuBigUInt(Workspace, words + 2);

        GpuBigIntMath.Sub(a, 2u, result);

        Assert.Throws<OverflowException>(() => Workspace.ThrowIfDeviceErrors());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1025)]
    [InlineData(2049)]
    [Scenario(TestScenario.Boundary)]
    public void InPlace_IsExact(int words)
    {
        // Below 2^(32 words - 1), so that doubling fits; doubling carries across every word boundary of a pass
        var value = (BigInteger.One << (32 * words - 1)) - 5;
        using var x = GpuBigUInt.FromBigInteger(Workspace, value, words);

        GpuBigIntMath.Mul(x, 2u, x);
        GpuBigIntMath.Add(x, 7u, x);
        GpuBigIntMath.Sub(x, 3u, x);

        Assert.Equal(value * 2 + 4, x.ToBigInteger());
        Workspace.ThrowIfDeviceErrors();
    }

    // In place, the borrow of 2^(32 words - 1) - 1 runs through every word, also across the passes of one block (1500,
    // 3000), and the result fits the operand: the exact value and no domain error.
    // mutant: GpuBigIntScalarKernels.cu.cs:58 `subtract == 1 ? 1u : 0u` -> `0u` (x - 2 in every word count)
    [Theory]
    [InlineData(3)]
    [InlineData(68)]
    [InlineData(1500)]
    [InlineData(3000)]
    [Scenario(TestScenario.Boundary)]
    public void Sub_OneFromTopBitOnlyInPlace_BorrowsThroughEveryWord(int words)
    {
        var x = BigInteger.One << (32 * words - 1);
        using var buffer = GpuBigUInt.FromBigInteger(Workspace, x, words);

        GpuBigIntMath.Sub(buffer, 1u, buffer);

        Assert.Equal(x - 1, buffer.ToBigInteger());
        Workspace.ThrowIfDeviceErrors();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task EqualWidth_WithCarry_IsReportedByAnAsynchronousRead()
    {
        var (value, scalar) = ScalarCases.OutOfDomain(ScalarOperation.Mul, 33);
        using var a = GpuBigUInt.FromBigInteger(Workspace, value, 33);
        using var result = new GpuBigUInt(Workspace, 33);

        GpuBigIntMath.Mul(a, scalar, result);

        await Assert.ThrowsAsync<OverflowException>(async () => await result.ToHostArrayAsync());
        await result.ToHostArrayAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CorrectionFailure_AndDomainError_AreThrownTogether()
    {
        var status = Workspace.CorrectionStatus ?? throw new InvalidOperationException("The workspace has no status.");
        var (value, scalar) = ScalarCases.OutOfDomain(ScalarOperation.Add, 2);
        using var a = GpuBigUInt.FromBigInteger(Workspace, value, 2);
        using var result = new GpuBigUInt(Workspace, 2);

        // One failure of the reciprocal correction, written into its words directly, and one domain error from a kernel
        Workspace.Provider.WriteWord(status.DevicePointer, 0, (uint)status.ReportedFailures + 1u, Workspace.Stream);
        Workspace.Provider.WriteWord(status.DevicePointer, 1, GpuBigIntDivRemKernels.ReciprocalCorrectionFailed,
            Workspace.Stream);
        GpuBigIntMath.Add(a, scalar, result);

        var error = Assert.Throws<AggregateException>(() => result.ToHostArray());
        Assert.Contains(error.InnerExceptions, static e => e is InvalidOperationException);
        Assert.Contains(error.InnerExceptions, static e => e is OverflowException);
        Workspace.ThrowIfDeviceErrors();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void IncrementOfTheLargestValue_AndDecrementOfZero_AreDomainErrors()
    {
        var max = new GpuBigUInt(Workspace, [uint.MaxValue, uint.MaxValue]);
        try
        {
            max++;
            Assert.Throws<OverflowException>(() => max.ToHostArray());
        }
        finally
        {
            max.Dispose();
        }

        var zero = new GpuBigUInt(Workspace, 2);
        try
        {
            zero--;
            Assert.Throws<OverflowException>(() => Workspace.ThrowIfDeviceErrors());
        }
        finally
        {
            zero.Dispose();
        }
    }
}

/// <summary>The scalar operations of <see cref="GpuBigIntMath"/> on the CPU fallback.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class ScalarResultWidthCpuFallbackTests
{
    public static TheoryData<ScalarOperation, int> OperationsAndWidths() =>
        ScalarResultWidthTests.OperationsAndWidths();

    [Theory]
    [MemberData(nameof(OperationsAndWidths))]
    [Scenario(TestScenario.Boundary)]
    public void ScalarResultWidth_CpuFallbackWithVaryingOperations_FollowsTheSameRules(
        ScalarOperation operation,
        int words)
    {
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(1 << 16);
            var (carry, carryScalar) = ScalarCases.CarryChain(operation, words);
            var (plain, plainScalar) = ScalarCases.NoCarry(operation, words);
            var (outside, outsideScalar) = ScalarCases.OutOfDomain(operation, words);
            using var a = GpuBigUInt.FromBigInteger(workspace, carry, words);
            using var b = GpuBigUInt.FromBigInteger(workspace, plain, words);
            using var c = GpuBigUInt.FromBigInteger(workspace, outside, words);

            // Narrower: rejected, nothing written
            using var buffer = new GpuBigUInt(workspace, Enumerable.Repeat(ScalarCases.Fill, words + 1).ToArray());
            if (words > 1)
            {
                Assert.Throws<ArgumentException>(() =>
                    ScalarCases.Apply(operation, a, carryScalar, ((GpuBigUIntView)buffer).Slice(0, words - 1)));
                Assert.All(buffer.ToHostArray(), static w => Assert.Equal(ScalarCases.Fill, w));
            }

            // Wider: exact
            ScalarCases.Apply(operation, a, carryScalar, buffer);
            Assert.Equal(ScalarCases.Limbs(ScalarCases.Oracle(operation, carry, carryScalar), words + 1),
                buffer.ToHostArray());

            // Equal width: exact without a carry, a domain error at the call with one
            using var same = new GpuBigUInt(workspace, words);
            ScalarCases.Apply(operation, b, plainScalar, same);
            Assert.Equal(ScalarCases.Limbs(ScalarCases.Oracle(operation, plain, plainScalar), words),
                same.ToHostArray());
            Assert.Throws<OverflowException>(() => ScalarCases.Apply(operation, c, outsideScalar, same));
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }
}

/// <summary>The scalar operation under test.</summary>
public enum ScalarOperation
{
    Add,
    Sub,
    Mul
}

/// <summary>Inputs and the oracle shared by the GPU and CPU fallback tests.</summary>
internal static class ScalarCases
{
    public const uint Fill = 0xDEADBEEFu;

    public static readonly int[] Widths = [1, 2, 33, 64, 1024, 1025, 2049];

    public static void Apply(ScalarOperation operation, GpuBigUIntView a, uint scalar, GpuBigUIntView result)
    {
        switch (operation)
        {
            case ScalarOperation.Add:
                GpuBigIntMath.Add(a, scalar, result);
                break;
            case ScalarOperation.Sub:
                GpuBigIntMath.Sub(a, scalar, result);
                break;
            default:
                GpuBigIntMath.Mul(a, scalar, result);
                break;
        }
    }

    public static BigInteger Oracle(ScalarOperation operation, BigInteger a, uint scalar) => operation switch
    {
        ScalarOperation.Add => a + scalar,
        ScalarOperation.Sub => a - scalar,
        _ => a * scalar
    };

    // A valid input whose carry (Add, Mul: the result needs one more word) or borrow (Sub) runs through every word
    public static (BigInteger Value, uint Scalar) CarryChain(ScalarOperation operation, int words) => operation switch
    {
        ScalarOperation.Add => ((BigInteger.One << (32 * words)) - 1, 1u),
        ScalarOperation.Sub => (BigInteger.One << (32 * words - 1), 1u),
        _ => ((BigInteger.One << (32 * words)) - 1, uint.MaxValue)
    };

    // An input whose result fits in the operand width, with a carry or borrow chain inside it
    public static (BigInteger Value, uint Scalar) NoCarry(ScalarOperation operation, int words) => operation switch
    {
        ScalarOperation.Add => ((BigInteger.One << (32 * words - 1)) - 1, 0x80000000u),
        ScalarOperation.Sub => (BigInteger.One << (32 * words - 2), 1u),
        _ => ((BigInteger.One << (32 * words - 2)) - 1, 3u)
    };

    // An input whose result does not fit in the operand width (Add, Mul) or is below zero (Sub), through every word
    public static (BigInteger Value, uint Scalar) OutOfDomain(ScalarOperation operation, int words) => operation switch
    {
        ScalarOperation.Sub => (BigInteger.Zero, 1u),
        _ => CarryChain(operation, words)
    };

    public static uint[] Limbs(BigInteger value, int words)
    {
        Assert.True(value.Sign >= 0 && value.GetBitLength() <= 32L * words, "The oracle value must fit the buffer.");
        var limbs = new uint[words];
        value.WriteLimbs(limbs);
        return limbs;
    }
}
