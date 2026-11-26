#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.MathDifferentialInputs;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for transcendental (<see cref="Math"/>, <see cref="MathF"/>) and <see cref="FastMath"/>
/// functions.
/// </summary>
public class MathTranscendentalDifferentialTests
{
    private static readonly (string Column, OutputPolicy Policy)[] s_singleTranscendentalColumnPolicies =
    [
        (
            "MathF.Sin(x)",
            OutputPolicy.Ulp(
                3,
                "CUDA Programming Guide sinf <= 2 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8)"
            )
        ),
        (
            "MathF.Cos(x)",
            OutputPolicy.Ulp(
                3,
                "CUDA Programming Guide cosf <= 2 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8)"
            )
        ),
        (
            "MathF.Tan(x)",
            OutputPolicy.Ulp(
                5,
                "CUDA Programming Guide tanf <= 4 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8)"
            )
        ),
        (
            "MathF.Exp(x)",
            OutputPolicy.Ulp(
                3,
                "CUDA Programming Guide expf <= 2 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8)",
                allowInfinityBoundary: true
            )
        ),
        (
            "MathF.Log(x)",
            OutputPolicy.Ulp(
                2,
                "CUDA Programming Guide logf <= 1 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8)"
            )
        ),
        (
            "MathF.Pow(x,y)",
            OutputPolicy.Ulp(
                5,
                "CUDA Programming Guide powf <= 4 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8)"
            )
        ),
    ];

    private static readonly (string Column, OutputPolicy Policy)[] s_doubleAndPromotedColumnPolicies =
    [
        (
            "Math.Sin(d)",
            OutputPolicy.Ulp(
                3,
                "CUDA Programming Guide sin <= 2 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8)"
            )
        ),
        (
            "Math.Cos(d)",
            OutputPolicy.Ulp(
                3,
                "CUDA Programming Guide cos <= 2 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8)"
            )
        ),
        (
            "Math.Tan(d)",
            OutputPolicy.Ulp(
                3,
                "CUDA Programming Guide tan <= 2 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8)"
            )
        ),
        (
            "Math.Exp(d)",
            OutputPolicy.Ulp(
                2,
                "CUDA Programming Guide exp <= 1 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8)",
                allowInfinityBoundary: true
            )
        ),
        (
            "Math.Log(d)",
            OutputPolicy.Ulp(
                2,
                "CUDA Programming Guide log <= 1 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8)"
            )
        ),
        (
            "Math.Pow(d1,d2)",
            OutputPolicy.Ulp(
                4,
                "CUDA Programming Guide pow <= 2 ulp + 2 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8)"
            )
        ),
        (
            "Math.Sin(f)",
            OutputPolicy.Ulp(
                3,
                "CUDA Programming Guide sin(double) <= 2 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8) (#876)"
            )
        ),
        (
            "Math.Cos(f)",
            OutputPolicy.Ulp(
                3,
                "CUDA Programming Guide cos(double) <= 2 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8) (#876)"
            )
        ),
        (
            "Math.Tan(f)",
            OutputPolicy.Ulp(
                3,
                "CUDA Programming Guide tan(double) <= 2 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8) (#876)"
            )
        ),
        (
            "Math.Exp(f)",
            OutputPolicy.Ulp(
                2,
                "CUDA Programming Guide exp(double) <= 1 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8) (#876)",
                allowInfinityBoundary: true
            )
        ),
        (
            "Math.Log(|f|+0.25f)",
            OutputPolicy.Ulp(
                2,
                "CUDA Programming Guide log(double) <= 1 ulp + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8) (#876)"
            )
        ),
        (
            "Math.Pow(|f|+0.25f,fExp)",
            OutputPolicy.Ulp(
                4,
                "CUDA Programming Guide pow(double) <= 2 ulp + 2 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8) (#876)"
            )
        ),
    ];

    private static readonly (string Column, OutputPolicy Policy)[] s_fastMathColumnPolicies =
    [
        ("FastMath.Sqrt(|x|)", OutputPolicy.Bitwise),
        ("FastMath.Abs(x)", OutputPolicy.Bitwise),
        (
            "FastMath.Exp(expX)",
            OutputPolicy.Ulp(
                14,
                "CUDA Programming Guide __expf <= 2 + floor(|1.173*x|) ulp on [-10,10] + 1 ulp margin "
                    + "for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)"
            )
        ),
        (
            "FastMath.Log(logUnitX)",
            OutputPolicy.Absolute(
                4.8e-7,
                "CUDA Programming Guide __logf abs error <= 2^-21.41 (~3.576e-7) on [0.5, 2.0] + host 1 ulp, the "
                    + "documented bound (measured max |__logf - MathF.Log| 1.8e-7 on every float of [0.5, 2], "
                    + "RTX 3070 Ti)"
            )
        ),
        (
            "FastMath.Log(logWideX)",
            OutputPolicy.Ulp(
                4,
                "CUDA Programming Guide __logf <= 3 ulp outside [0.5,2.0] + 1 ulp margin for the host result "
                    + "(measured CUDA vs .NET: at most 1 ulp, seeds 0-8)"
            )
        ),
    ];

    // Transcendental and Pow special values / fixed points (Bitwise)
    internal static readonly DifferentialCase s_transcendentalSpecialValues =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.TranscendentalSpecialValuesKernel)}")
        {
            Shape = LaunchShape.ForRows(64, blockX: 32),
            Inputs = static (rng, rows) =>
            {
                var fx = DiffValues.Cycled(
                    rng,
                    rows,
                    TrigExpSpecialSingles,
                    static r => TrigExpSpecialSingles[r.NextBelow(TrigExpSpecialSingles.Length)]
                );
                var logF = DiffValues.Cycled(
                    rng,
                    rows,
                    LogSpecialSingles,
                    static r => LogSpecialSingles[r.NextBelow(LogSpecialSingles.Length)]
                );
                var powBaseF = new float[rows];
                var powExpF = new float[rows];
                var dx = DiffValues.Cycled(
                    rng,
                    rows,
                    TrigExpSpecialDoubles,
                    static r => TrigExpSpecialDoubles[r.NextBelow(TrigExpSpecialDoubles.Length)]
                );
                var logD = DiffValues.Cycled(
                    rng,
                    rows,
                    LogSpecialDoubles,
                    static r => LogSpecialDoubles[r.NextBelow(LogSpecialDoubles.Length)]
                );
                var powBaseD = new double[rows];
                var powExpD = new double[rows];

                for (int i = 0; i < rows; i++)
                {
                    int pfIdx = rng.IsBoundarySeed || rng.NextBool()
                        ? i % PowSpecialSinglePairs.Length
                        : rng.NextBelow(PowSpecialSinglePairs.Length);
                    powBaseF[i] = PowSpecialSinglePairs[pfIdx].Base;
                    powExpF[i] = PowSpecialSinglePairs[pfIdx].Exp;

                    int pdIdx = rng.IsBoundarySeed || rng.NextBool()
                        ? i % PowSpecialDoublePairs.Length
                        : rng.NextBelow(PowSpecialDoublePairs.Length);
                    powBaseD[i] = PowSpecialDoublePairs[pdIdx].Base;
                    powExpD[i] = PowSpecialDoublePairs[pdIdx].Exp;
                }

                return new DiffBuffers()
                    .In("fx", fx)
                    .In("logF", logF)
                    .In("powBaseF", powBaseF)
                    .In("powExpF", powExpF)
                    .In("dx", dx)
                    .In("logD", logD)
                    .In("powBaseD", powBaseD)
                    .In("powExpD", powExpD)
                    .Out<float>(
                        "rf",
                        rows,
                        MathDifferentialKernels.TranscendentalSpecialSingleColumns,
                        OutputPolicy.Bitwise
                    )
                    .Out<double>(
                        "rd",
                        rows,
                        MathDifferentialKernels.TranscendentalSpecialDoubleColumns,
                        OutputPolicy.Bitwise
                    );
            },
            Launch = static (b, s) => MathDifferentialKernels.LaunchTranscendentalSpecialValuesKernel(
                singleInputs: new TranscendentalSingleSpecialInputs
                {
                    GeneralValues = b.Get<float>("fx"),
                    LogValues = b.Get<float>("logF"),
                    PowerBases = b.Get<float>("powBaseF"),
                    PowerExponents = b.Get<float>("powExpF"),
                },
                doubleInputs: new TranscendentalDoubleSpecialInputs
                {
                    GeneralValues = b.Get<double>("dx"),
                    LogValues = b.Get<double>("logD"),
                    PowerBases = b.Get<double>("powBaseD"),
                    PowerExponents = b.Get<double>("powExpD"),
                },
                singleResults: b.Get<float>("rf"),
                doubleResults: b.Get<double>("rd"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
        };

    // MathF single-precision transcendentals (ULP + Verify)
    internal static readonly DifferentialCase s_singleTranscendental =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.SingleTranscendentalMatrixKernel)}")
        {
            Shape = LaunchShape.ForRows(256, blockX: 64),
            Inputs = static (rng, rows) =>
            {
                var trigX = DiffValues.Mixed(rng, rows, TrigDomainSingleBoundaries, NextSafeTrigSingle);
                var expX = DiffValues.Mixed(
                    rng,
                    rows,
                    ExpDomainSingleBoundaries,
                    static r => -104.0f + (float)(r.NextUInt32() / (double)uint.MaxValue) * 192.5f
                );
                var logX = DiffValues.Mixed(rng, rows, LogDomainSingleBoundaries, NextPositiveSingle);
                var powBase = new float[rows];
                var powExp = new float[rows];

                for (int i = 0; i < rows; i++)
                {
                    double uBase = rng.IsBoundarySeed ? (i % 64) / 64.0 : rng.NextUInt32() / (double)uint.MaxValue;
                    double uExp = rng.IsBoundarySeed ? ((i * 7) % 64) / 64.0 : rng.NextUInt32() / (double)uint.MaxValue;
                    powBase[i] = 0.1f + (float)(uBase * 9.9);
                    powExp[i] = -5.0f + (float)(uExp * 10.0);
                }

                if (rows > 2)
                {
                    expX[0] = -95.0f;
                    logX[0] = float.Epsilon;
                    trigX[0] = 1.0e-20f;
                }

                return new DiffBuffers()
                    .In("trigX", trigX)
                    .In("expX", expX)
                    .In("logX", logX)
                    .In("powBase", powBase)
                    .In("powExp", powExp)
                    .Out<float>("r", rows, s_singleTranscendentalColumnPolicies);
            },
            Launch = static (b, s) => MathDifferentialKernels.LaunchSingleTranscendentalMatrixKernel(
                inputs: new SingleTranscendentalInputs
                {
                    TrigArguments = b.Get<float>("trigX"),
                    ExpArguments = b.Get<float>("expX"),
                    LogArguments = b.Get<float>("logX"),
                    PowerBases = b.Get<float>("powBase"),
                    PowerExponents = b.Get<float>("powExp"),
                },
                results: b.Get<float>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
            Verify = static (b, s) =>
            {
                var logX = b.Get<float>("logX");
                var r = b.Get<float>("r");
                for (int i = 0; i < s.Rows; i++)
                {
                    int o = i * 6;
                    float sin = r[o];
                    float cos = r[o + 1];
                    float logVal = r[o + 4];
                    if (MathF.Abs(sin) > 1.000001f || MathF.Abs(cos) > 1.000001f)
                    {
                        return PropertyResult.Fail(
                            $"row {i}: |sin|={MathF.Abs(sin)} or |cos|={MathF.Abs(cos)} exceeds 1"
                        );
                    }

                    float pythagoras = MathF.Abs(sin * sin + cos * cos - 1.0f);
                    if (pythagoras > 8.0f * 1.1920929e-7f)
                    {
                        return PropertyResult.Fail($"row {i}: |sin^2 + cos^2 - 1| = {pythagoras} exceeds bound");
                    }

                    if (logX[i] > 0.0f && logX[i] < 1.17549435e-38f && (!float.IsFinite(logVal) || logVal >= -87.3f))
                    {
                        return PropertyResult.Fail($"row {i}: subnormal log({logX[i]}) produced {logVal}");
                    }
                }

                return PropertyResult.Pass();
            },
        };

    // System.Math double-precision transcendentals + float argument widening (#876)
    internal static readonly DifferentialCase s_doubleAndPromotedSingleTranscendental = new(
        $"{nameof(MathDifferentialKernels)}."
            + $"{nameof(MathDifferentialKernels.DoubleAndPromotedSingleTranscendentalMatrixKernel)}"
    )
    {
        Shape = LaunchShape.ForRows(256, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var trigD = DiffValues.Mixed(rng, rows, TrigDomainDoubleBoundaries, NextSafeTrigDouble);
            var expD = DiffValues.Mixed(
                rng,
                rows,
                ExpDomainDoubleBoundaries,
                static r => -744.0 + (r.NextUInt64() / (double)ulong.MaxValue) * 1453.0
            );
            var logD = DiffValues.Mixed(rng, rows, LogDomainDoubleBoundaries, NextPositiveDouble);
            var powBaseD = new double[rows];
            var powExpD = new double[rows];
            var fArg = DiffValues.Mixed(rng, rows, TrigDomainSingleBoundaries, NextSafeTrigSingle);
            var fExp = new float[rows];

            for (int i = 0; i < rows; i++)
            {
                double uBase = rng.IsBoundarySeed ? (i % 64) / 64.0 : rng.NextUInt64() / (double)ulong.MaxValue;
                double uExp = rng.IsBoundarySeed ? ((i * 7) % 64) / 64.0 : rng.NextUInt64() / (double)ulong.MaxValue;
                double uFExp = rng.IsBoundarySeed ? ((i * 5) % 64) / 64.0 : rng.NextUInt32() / (double)uint.MaxValue;
                powBaseD[i] = 0.1 + (uBase * 9.9);
                powExpD[i] = -5.0 + (uExp * 10.0);
                fExp[i] = -4.0f + (float)(uFExp * 8.0);
            }

            if (rows > 0)
            {
                expD[0] = -720.0;
                logD[0] = double.Epsilon;
                fArg[0] = 0.5f;
                fExp[0] = 1.5f;
            }

            return new DiffBuffers()
                .In("trigD", trigD)
                .In("expD", expD)
                .In("logD", logD)
                .In("powBaseD", powBaseD)
                .In("powExpD", powExpD)
                .In("fArg", fArg)
                .In("fExp", fExp)
                .Out<double>("r", rows, s_doubleAndPromotedColumnPolicies);
        },
        Launch = static (b, s) => MathDifferentialKernels.LaunchDoubleAndPromotedSingleTranscendentalMatrixKernel(
            doubleInputs: new DoubleTranscendentalInputs
            {
                TrigArguments = b.Get<double>("trigD"),
                ExpArguments = b.Get<double>("expD"),
                LogArguments = b.Get<double>("logD"),
                PowerBases = b.Get<double>("powBaseD"),
                PowerExponents = b.Get<double>("powExpD"),
            },
            promotedSingleInputs: new PromotedSingleTranscendentalInputs
            {
                Arguments = b.Get<float>("fArg"),
                Exponents = b.Get<float>("fExp"),
            },
            results: b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        OracleIsLaunch = true,
    };

    // FastMath (Sqrt, Abs, Exp, Log)
    internal static readonly DifferentialCase s_fastMath =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.FastMathMatrixKernel)}")
        {
            Shape = LaunchShape.ForRows(128, blockX: 64),
            Inputs = static (rng, rows) =>
            {
                var x = DiffValues.Mixed(rng, rows, DiffValues.SubnormalSingleBoundaries, DiffValues.NextSingle);
                var expX = new float[rows];
                var logUnitX = new float[rows];
                var logWideX = new float[rows];

                for (int i = 0; i < rows; i++)
                {
                    double u1 = rng.IsBoundarySeed ? (i % 64) / 63.0 : rng.NextUInt32() / (double)uint.MaxValue;
                    double u2 = rng.IsBoundarySeed ? ((i * 5) % 64) / 63.0 : rng.NextUInt32() / (double)uint.MaxValue;
                    expX[i] = (float)(-10.0 + u1 * 20.0);
                    logUnitX[i] = (float)(0.5 + u2 * 1.5);
                    int expIdx = rng.IsBoundarySeed ? (i % 38) : rng.NextBelow(38);
                    // [-21..-3] U [2..20], strictly outside [0.5, 2.0]
                    int expPow = expIdx < 19 ? (expIdx - 21) : (expIdx - 17);
                    logWideX[i] = MathF.ScaleB((float)(1.0 + u1 * 0.999), expPow);
                }

                if (rows > 0)
                {
                    expX[0] = 10.0f;
                    logUnitX[0] = 0.75f;
                    logWideX[0] = 1.0e6f;
                }

                return new DiffBuffers()
                    .In("x", x)
                    .In("expX", expX)
                    .In("logUnitX", logUnitX)
                    .In("logWideX", logWideX)
                    .Out<float>("r", rows, s_fastMathColumnPolicies);
            },
            Launch = static (b, s) => MathDifferentialKernels.LaunchFastMathMatrixKernel(
                inputs: new FastMathInputs
                {
                    Values = b.Get<float>("x"),
                    ExpArguments = b.Get<float>("expX"),
                    UnitLogArguments = b.Get<float>("logUnitX"),
                    WideLogArguments = b.Get<float>("logWideX"),
                },
                results: b.Get<float>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
            Verify = static (b, s) =>
            {
                var logUnitX = b.Get<float>("logUnitX");
                var r = b.Get<float>("r");
                // CUDA Programming Guide __logf abs error <= 2^-21.41 (~3.576e-7) on [0.5, 2.0] + host 1 ulp
                const float maxAbsError = 4.8e-7f;
                for (int i = 0; i < s.Rows; i++)
                {
                    float actual = r[i * 5 + 3];
                    float expected = MathF.Log(logUnitX[i]);
                    float diff = MathF.Abs(actual - expected);
                    if (diff > maxAbsError)
                    {
                        return PropertyResult.Fail(
                            $"row {i} (x={logUnitX[i]}): FastMath.Log={actual}, expected={expected}, diff={diff}"
                        );
                    }
                }

                return PropertyResult.Pass();
            },
        };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_TranscendentalSpecialValues_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_transcendentalSpecialValues);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_SingleTranscendental_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singleTranscendental);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_DoubleAndPromotedSingleTranscendental_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_doubleAndPromotedSingleTranscendental);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_FastMath_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_fastMath);
}
