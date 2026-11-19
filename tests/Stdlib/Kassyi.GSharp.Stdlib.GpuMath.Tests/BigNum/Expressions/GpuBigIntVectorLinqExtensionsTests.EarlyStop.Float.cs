#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Any and All stop at the deciding element (see EarlyStop.Int.cs). This file: float chains, whose element operations
// run on the host.
public partial class GpuBigIntVectorLinqExtensionsTests
{
    public enum FloatChain
    {
        ZipDivideAboveZero,
        LogAboveOne,
        ZipPowAboveOne,
        LogDividedByZeroAboveZero,
    }

    /// <summary>Any (<paramref name="All"/> false) or All of a float chain over <paramref name="X"/> (and Y).</summary>
    public sealed record FloatCase(string Name, FloatChain Chain, long[] X, long[] Y, bool All);

    private static readonly FloatCase[] s_floatCases =
    [
        // D = 0 before T = 1
        new("ZipDivideAnyDecides", FloatChain.ZipDivideAboveZero, [1, 2, 3, 4], [1, 0, 3, 4], All: false),
        // T = 1 before D
        new("ZipDivideAnyThrows", FloatChain.ZipDivideAboveZero, [0, 2, 3], [1, 0, 3], All: false),
        // D = 0 before T = 1
        new("ZipDivideAllDecides", FloatChain.ZipDivideAboveZero, [0, 2, 3], [1, 0, 3], All: true),
        // ln(4) > 1 at D = 0 before the logarithm of -4 throws at T = 1.
        // mutant: FloatVectorElementwiseHelper.ExecuteUnary rethrowing instead of recording
        new("LogAnyDecides", FloatChain.LogAboveOne, [4, -4, 16], [], All: false),
        // T = 1 before any element decides
        new("LogAnyThrows", FloatChain.LogAboveOne, [1, -4, 16], [], All: false),
        // ln(1) = 0 decides All at D = 0
        new("LogAllDecides", FloatChain.LogAboveOne, [1, -4, 16], [], All: true),
        // 4^2 > 1 at D = 0 before the negative base throws at T = 1.
        // mutant: FloatVectorElementwiseHelper.ExecuteBinary rethrowing instead of recording
        new("ZipPowAnyDecides", FloatChain.ZipPowAboveOne, [4, -2, 9], [2, 2, 2], All: false),
        // Element 0 divides by zero before the logarithm of element 1 throws.
        // mutant: FloatVectorDivByUInt32Expression throwing instead of recording (InvalidOperationException)
        new("LogDividedByZeroAnyThrowsDivision", FloatChain.LogDividedByZeroAboveZero, [4, -4], [], All: false),
    ];

    public static TheoryData<string> FloatCaseNames => new(s_floatCases.Select(static c => c.Name));

    [Theory]
    [MemberData(nameof(FloatCaseNames))]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.SadPath)]
    public void AnyAndAll_FloatChainCase_MatchSystemLinq(string name)
    {
        var c = s_floatCases.Single(c => c.Name == name);
        var expected = Outcome(() => FloatReference(c));

        using var x = CreateFloatArray(c.X);
        using var y = CreateFloatArray(c.Y.Length == 0 ? c.X : c.Y);
        using var zero = GpuBigFloatTestHelpers.CreateFloat(_context, 0);
        using var one = GpuBigFloatTestHelpers.CreateFloat(_context, 1);
        var xs = x.AsVectorizedEnumerable();
        var flags = c.Chain switch
        {
            FloatChain.ZipDivideAboveZero => xs.ZipDivide(y.AsVectorizedEnumerable()).IsGreaterThan(zero),
            FloatChain.LogAboveOne => xs.SelectLog().IsGreaterThan(one),
            FloatChain.ZipPowAboveOne => xs.ZipPow(y.AsVectorizedEnumerable()).IsGreaterThan(one),
            _ => xs.SelectLog().SelectDivideByUInt32(0u).IsGreaterThan(zero),
        };

        using var quantifier = c.All ? flags.All() : flags.Any();
        Assert.Equal(expected, Outcome(() => quantifier.Evaluate(_context)));
    }

    private static bool FloatReference(FloatCase c)
    {
        var flags = Enumerable.Range(0, c.X.Length).Select(i => c.Chain switch
        {
            FloatChain.ZipDivideAboveZero => (decimal)c.X[i] / c.Y[i] > 0m,
            FloatChain.LogAboveOne => FloatLogContract(c.X[i]) > 1.0,
            FloatChain.ZipPowAboveOne => FloatPowContract(c.X[i], c.Y[i]) > 1.0,
            _ => (decimal)FloatLogContract(c.X[i]) / 0m > 0m,
        });
        return c.All ? flags.All(static f => f) : flags.Any(static f => f);
    }

    private GpuBigFloatArray CreateFloatArray(long[] values, int numWords = 4)
    {
        var array = new GpuBigFloatArray(_context, values.Length, numWords);
        for (var i = 0; i < values.Length; i++)
        {
            using var element =
                GpuBigFloatTestHelpers.CreateFloat(_context, values[i], exponent: 0, numWords: numWords);
            array.SetElement(i, element);
        }

        return array;
    }

    private static double FloatLogContract(long x) =>
        x <= 0 ? throw new InvalidOperationException("Log of non-positive number.") : Math.Log(x);

    private static double FloatPowContract(long x, long y) =>
        x < 0 ? throw new InvalidOperationException("Pow with negative base is not supported.") : Math.Pow(x, y);
}
