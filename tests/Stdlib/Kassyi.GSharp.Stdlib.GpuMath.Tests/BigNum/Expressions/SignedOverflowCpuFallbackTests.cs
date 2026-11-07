#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// The overflow check of the signed <see cref="GpuBigIntExpr"/> sums in CPU fallback mode, which has no device status
/// words: the evaluation itself throws the same <see cref="GpuOverflowException"/>.
/// </summary>
/// <remarks><see cref="GpuKernelLoader.ForceCpuFallback"/> is AsyncLocal, so each test sets and restores it itself.</remarks>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class SignedOverflowCpuFallbackTests
{
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("+", 1L)]
    [InlineData("-", -1L)]
    public void MagnitudesAdded_CarryOut_TheEvaluationThrows(string op, long b)
    {
        // mutant: SignedMagnitude.ThrowIfWrapped `result.CompareTo(operand) < 0` -> `> 0`
        var (thrown, line) = RunInFallback(workspace =>
        {
            using var x = Create(workspace, uint.MaxValue);
            using var y = Create(workspace, b);
            var expression = op == "+" ? (GpuBigIntExpr)x + y : (GpuBigIntExpr)x - y;

            var error = Record.Exception(() => expression.Evaluate(workspace).Dispose());
            return (error, Line() - 1); // the line of Evaluate, which throws before a tuple on its line could complete
        });

        var overflow = Assert.IsType<GpuOverflowException>(thrown);
        Assert.Contains($"`{op}` result does not fit in 1 word(s)", overflow.Message);
        Assert.Contains($"{nameof(SignedOverflowCpuFallbackTests)}.cs:line {line}", overflow.Message);
        Assert.Contains("CPU fallback mode", overflow.Message);
        Assert.Equal(("Evaluate", $"[x0 {op} x1]"), (overflow.Origins[0].EntryPoint, overflow.Origins[0].Expression));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void BitwiseNot_AllOnesMagnitude_TheEvaluationThrows()
    {
        // ~0xFFFFFFFF is -0x100000000, which needs another word.
        // mutant: SignedMagnitude.IncrementChecked without ThrowIfWrapped on the host path
        var (thrown, value) = RunInFallback(workspace =>
        {
            using var x = Create(workspace, uint.MaxValue);
            using var y = Create(workspace, 5);
            BigInteger notFive = 0;
            var error = Record.Exception(() =>
            {
                using (var fits = (~(GpuBigIntExpr)y).Evaluate(workspace))
                {
                    notFive = fits.Value.ToBigInteger(fits.IsNegative);
                }

                (~(GpuBigIntExpr)x).Evaluate(workspace).Dispose();
            });
            return (error, notFive);
        });

        Assert.Equal(new BigInteger(-6), value);
        var overflow = Assert.IsType<GpuOverflowException>(thrown);
        Assert.Equal(("~", "[~x0]"), (overflow.Origins[0].Operation, overflow.Origins[0].Expression));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Sum_ThatFits_IsExact()
    {
        // mutant: SignedMagnitude.ThrowIfWrapped `< 0` -> `<= 0`
        //         (a sum equal to an operand would throw)
        var (thrown, value) = RunInFallback(workspace =>
        {
            using var x = Create(workspace, uint.MaxValue - 1);
            using var one = Create(workspace, 1);
            using var zero = Create(workspace, 0);
            BigInteger sum = 0;
            var error = Record.Exception(() =>
            {
                using var fits = ((GpuBigIntExpr)x + one).Evaluate(workspace);
                using var same = ((GpuBigIntExpr)fits + zero).Evaluate(workspace);
                sum = same.Value.ToBigInteger();
            });
            return (error, sum);
        });

        Assert.Null(thrown);
        Assert.Equal(new BigInteger(uint.MaxValue), value);
    }

    private static T RunInFallback<T>(Func<GpuWorkspace, T> body)
    {
        IDisposable? outerContextScope = GpuKernelLoader.IsDriverAvailable
            ? new GpuKernelLoader.CudaContextScope()
            : null;
        try
        {
            GpuKernelLoader.ForceCpuFallback = true;
            try
            {
                using var workspace = new GpuWorkspace(64);
                return body(workspace);
            }
            finally
            {
                GpuKernelLoader.ForceCpuFallback = false;
            }
        }
        finally
        {
            outerContextScope?.Dispose();
        }
    }

    private static int Line([CallerLineNumber] int line = 0) => line;

    private static GpuBigInt Create(GpuWorkspace workspace, BigInteger value) =>
        new(GpuBigUInt.FromBigInteger(workspace, BigInteger.Abs(value), 1), value.Sign < 0);
}
