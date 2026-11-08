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
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public ScalarResultWidthTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

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
        using var a = new GpuBigUInt(_workspace, [5u, 6u, 7u, 8u]);
        using var buffer = new GpuBigUInt(_workspace, Enumerable.Repeat(ScalarCases.Fill, 8).ToArray());

        foreach (var resultWords in new[] { 1, 3 })
        {
            Assert.Throws<ArgumentException>(() =>
                ScalarCases.Apply(operation, a, 2u, ((GpuBigUIntView)buffer).Slice(0, resultWords)));
        }

        Assert.All(buffer.ToHostArray(), static w => Assert.Equal(ScalarCases.Fill, w));
        _workspace.ThrowIfDeviceErrors();
    }

    [Theory]
    [MemberData(nameof(OperationsAndWidths))]
    [Scenario(TestScenario.Boundary)]
    public void WiderResult_HoldsTheExactValue_AndZerosAbove(ScalarOperation operation, int words)
    {
        var (value, scalar) = ScalarCases.CarryChain(operation, words);
        using var a = GpuBigUInt.FromBigInteger(_workspace, value, words);

        foreach (var extra in new[] { 1, 3 })
        {
            using var result = new GpuBigUInt(_workspace, Enumerable.Repeat(ScalarCases.Fill, words + extra).ToArray());
            ScalarCases.Apply(operation, a, scalar, result);

            Assert.Equal(ScalarCases.Limbs(ScalarCases.Oracle(operation, value, scalar), words + extra),
                result.ToHostArray());
        }

        _workspace.ThrowIfDeviceErrors();
    }

    [Theory]
    [MemberData(nameof(OperationsAndWidths))]
    [Scenario(TestScenario.HappyPath)]
    public void EqualWidth_WithoutCarry_IsExact(ScalarOperation operation, int words)
    {
        var (value, scalar) = ScalarCases.NoCarry(operation, words);
        using var a = GpuBigUInt.FromBigInteger(_workspace, value, words);
        using var result = new GpuBigUInt(_workspace, Enumerable.Repeat(ScalarCases.Fill, words).ToArray());

        ScalarCases.Apply(operation, a, scalar, result);

        Assert.Equal(ScalarCases.Limbs(ScalarCases.Oracle(operation, value, scalar), words), result.ToHostArray());
        _workspace.ThrowIfDeviceErrors();
    }

    [Theory]
    [MemberData(nameof(OperationsAndWidths))]
    [Scenario(TestScenario.SadPath)]
    public void EqualWidth_WithCarry_IsReportedOnceAtTheNextRead(ScalarOperation operation, int words)
    {
        var (value, scalar) = ScalarCases.OutOfDomain(operation, words);
        using var a = GpuBigUInt.FromBigInteger(_workspace, value, words);
        using var result = new GpuBigUInt(_workspace, words);

        ScalarCases.Apply(operation, a, scalar, result);

        var error = Assert.Throws<OverflowException>(() => result.ToHostArray());
        Assert.Contains($"GpuBigIntMath.{operation}", error.Message);

        // Reported once: the next read and an explicit check pass
        result.ToHostArray();
        _workspace.ThrowIfDeviceErrors();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(33)]
    [InlineData(2049)]
    [Scenario(TestScenario.SadPath)]
    public void Subtraction_BelowZero_IsReported_AlsoIntoAWiderResult(int words)
    {
        using var a = GpuBigUInt.FromBigInteger(_workspace, 1, words);
        using var result = new GpuBigUInt(_workspace, words + 2);

        GpuBigIntMath.Sub(a, 2u, result);

        Assert.Throws<OverflowException>(() => _workspace.ThrowIfDeviceErrors());
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
        using var x = GpuBigUInt.FromBigInteger(_workspace, value, words);

        GpuBigIntMath.Mul(x, 2u, x);
        GpuBigIntMath.Add(x, 7u, x);
        GpuBigIntMath.Sub(x, 3u, x);

        Assert.Equal(value * 2 + 4, x.ToBigInteger());
        _workspace.ThrowIfDeviceErrors();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task EqualWidth_WithCarry_IsReportedByAnAsynchronousRead()
    {
        var (value, scalar) = ScalarCases.OutOfDomain(ScalarOperation.Mul, 33);
        using var a = GpuBigUInt.FromBigInteger(_workspace, value, 33);
        using var result = new GpuBigUInt(_workspace, 33);

        GpuBigIntMath.Mul(a, scalar, result);

        await Assert.ThrowsAsync<OverflowException>(async () => await result.ToHostArrayAsync());
        await result.ToHostArrayAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CorrectionFailure_AndDomainError_AreThrownTogether()
    {
        var status = _workspace.CorrectionStatus ?? throw new InvalidOperationException("The workspace has no status.");
        var (value, scalar) = ScalarCases.OutOfDomain(ScalarOperation.Add, 2);
        using var a = GpuBigUInt.FromBigInteger(_workspace, value, 2);
        using var result = new GpuBigUInt(_workspace, 2);

        // One failure of the reciprocal correction, written into its words directly, and one domain error from a kernel
        _workspace.Provider.WriteWord(status.DevicePointer, 0, (uint)status.ReportedFailures + 1u, _workspace.Stream);
        _workspace.Provider.WriteWord(status.DevicePointer, 1, GpuBigIntDivRemKernels.ReciprocalCorrectionFailed,
            _workspace.Stream);
        GpuBigIntMath.Add(a, scalar, result);

        var error = Assert.Throws<AggregateException>(() => result.ToHostArray());
        Assert.Contains(error.InnerExceptions, static e => e is InvalidOperationException);
        Assert.Contains(error.InnerExceptions, static e => e is OverflowException);
        _workspace.ThrowIfDeviceErrors();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void IncrementOfTheLargestValue_AndDecrementOfZero_AreDomainErrors()
    {
        var max = new GpuBigUInt(_workspace, [uint.MaxValue, uint.MaxValue]);
        try
        {
            max++;
            Assert.Throws<OverflowException>(() => max.ToHostArray());
        }
        finally
        {
            max.Dispose();
        }

        var zero = new GpuBigUInt(_workspace, 2);
        try
        {
            zero--;
            Assert.Throws<OverflowException>(() => _workspace.ThrowIfDeviceErrors());
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
    public void FollowsTheSameRules(ScalarOperation operation, int words)
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
