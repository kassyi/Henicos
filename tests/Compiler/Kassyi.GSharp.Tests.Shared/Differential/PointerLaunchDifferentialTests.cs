#nullable enable
using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// <c>Launch{Kernel}Ptr</c> takes each array as a <c>GSharpArrayProxy</c>: the kernel sees the length the proxy carries,
/// as the array launcher passes the array's length, and the launcher rejects a proxy that describes no array (#720).
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public partial class PointerLaunchDifferentialTests
{
    private const int LongLength = 8;
    private const int ShortLength = 5;

    internal static readonly DifferentialCase s_twoLengths = new(
        $"{nameof(PointerLaunchDifferentialKernels)}.{nameof(PointerLaunchDifferentialKernels.TwoLengthsKernel)}")
    {
        Shape = LaunchShapes.Linear(LongLength, blockX: 32),
        Seeds = [0, 1],
        Inputs = static (rng, _) =>
        {
            // Seed 1 swaps the lengths, so each argument is the longer one once
            var (aLength, bLength) = rng.Seed == 0 ? (LongLength, ShortLength) : (ShortLength, LongLength);
            int[] a = new int[aLength];
            int[] b = new int[bLength];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = i + 1;
            }

            for (int i = 0; i < b.Length; i++)
            {
                b[i] = 100 * (i + 1);
            }

            return new DiffBuffers()
                .In("a", a, OutputPolicy.Exact)
                .In("b", b, OutputPolicy.Exact)
                .Out<int>("lengths", 2, OutputPolicy.Exact);
        },
        OracleIsLaunch = true,
        Launch = static (b, s) => PointerLaunchDifferentialKernels.LaunchTwoLengthsKernel(
            b.Get<int>("a"),
            b.Get<int>("b"),
            b.Get<int>("lengths"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        LaunchPtr = GetTwoLengthsLaunchPtr(),
    };

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LaunchPtr_MultipleArrays_EachArraySeesItsOwnLength() => DifferentialRunner.Run(s_twoLengths);

    internal static readonly DifferentialCase s_optionalLength = new(
        $"{nameof(PointerLaunchDifferentialKernels)}.{nameof(PointerLaunchDifferentialKernels.OptionalLengthKernel)}")
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0, 1, 2],
        Inputs = static (rng, _) => new DiffBuffers()
            .InNullable("optional", rng.Seed switch
            {
                0 => null,
                1 => Array.Empty<int>(),
                _ => new[] { 4, 5, 6 }
            })
            .Out<int>("result", 1, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) => PointerLaunchDifferentialKernels.LaunchOptionalLengthKernel(
            b.GetNullable<int>("optional"),
            b.Get<int>("result"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        LaunchPtr = GetOptionalLengthLaunchPtr(),
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LaunchPtr_OptionalArrays_NullAndEmptyArraysStayApart() => DifferentialRunner.Run(s_optionalLength);

    private static partial Action<DiffBuffers, LaunchShape>? GetTwoLengthsLaunchPtr();
    private static partial Action<DiffBuffers, LaunchShape>? GetOptionalLengthLaunchPtr();
}
