#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Tests of the host implementation of the big-float Log / Exp (<see cref="BigFloatTranscendentalMath"/>) that need no
/// GPU. The GPU and CPU providers are compared in <see cref="GpuBigFloatTranscendentalTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class BigFloatTranscendentalMathTests
{
    // The ZivRetry inputs lie next to a p-bit boundary, closer than the error bound of the first approximation: the
    // rounding check rejects it, retries with more guard words, and the result is RZ_p of the exact value (oracle).
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ZivRetryInputs.Names), MemberType = typeof(ZivRetryInputs))]
    // mutant: BigFloatTranscendentalMath.cs TryTruncate `ErrorBound <= guard && guard + ErrorBound < limit` ->
    // `ErrorBound <= guard` (upper half dropped): LogBelow, LogAbove, ExpBelow, ExpAbove (for Above the accepted
    // truncation is one ulp low)
    // mutant: the same line -> `guard + ErrorBound < limit` (lower half dropped): LogNegativeBelow, LogNegativeAbove,
    // LogDistanceAbove, ExpDistanceAbove (accepted at the first attempt whatever the sign of its error)
    public void ZivRetry_RetriesAndIsRz(string caseName)
    {
        var (kind, sig, exp, negative) = ZivRetryInputs.Cases[caseName];
        var options = new GpuBigFloatTranscendentalOptions();

        int attempts;
        if (kind == "Log")
        {
            var (s, e, n) = BigFloatTranscendentalMath.Log(sig, exp, 128, options, out attempts);
            Assert.True(BigFloatIntervalOracle.IsRzOfLog(sig, exp, s, e, n, 128), $"{caseName}: ({s:X}, {e}, {n}) is not RZ(ln x)");
        }
        else
        {
            var (s, e) = BigFloatTranscendentalMath.Exp(sig, exp, negative, 128, options, out attempts);
            var (num, denShift) = BigFloatIntervalOracle.ToRatio(sig, exp);
            Assert.Equal(BigFloatIntervalOracle.ExpRz(negative ? -num : num, denShift, 128), (s, (long)e));
        }

        Assert.True(attempts >= 2, $"{caseName}: the first approximation was accepted");
    }
}
