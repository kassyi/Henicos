#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The fake case and the host paths shared by the tests of the differential harness itself
/// (<see cref="DifferentialRunnerTests"/>, <see cref="DifferentialKnownDivergencesTests"/>,
/// <see cref="DifferentialExceptionTests"/>, <see cref="DifferentialPolicyTests"/>). In those tests each check names,
/// above it, the one-line change of the harness (mutant) that makes it fail, as property-test-design §5 asks. The fakes
/// compute on the host, so the tests that use them need neither a GPU nor a native build.
/// </summary>
internal static class DifferentialHarnessFakes
{
    public const string FakeCaseName = "HarnessTests.FakeAddKernel";

    private static readonly string[] s_fakeColumns = ["sum", "xor"];

    /// <summary>5 rows in 3 blocks of 2: the last thread has no row and must return without writing.</summary>
    public static LaunchShape FakeShape { get; } = LaunchShape.ForRows(5, blockX: 2);

    /// <summary>A host "path": the launch runs as it is, on the calling thread.</summary>
    public static IReadOnlyList<DiffPathRunner> NativeOnly { get; } = [new(DiffPath.Native, DiffPaths.RunDirect)];

    /// <summary>No known divergence.</summary>
    public static IReadOnlyList<KnownDivergence> NoEntries { get; } = [];

    /// <summary>Adds one to every sum cell (the even cells) of the fake case's output.</summary>
    public static void CorruptSums(int[] r)
    {
        for (int i = 0; i < r.Length; i += 2)
        {
            r[i]++;
        }
    }

    // r[t * 2] = a + b, r[t * 2 + 1] = a ^ b for 5 rows. The oracle is a kernel run as C# (Int32AddXorKernel); the
    // launch computes on the host, then applies corrupt to r when it runs on one of the corrupted paths
    public static FakeRun FakeCase(
        DiffPath paths = DiffPath.Managed | DiffPath.Native,
        Action<int[]>? corrupt = null,
        DiffPath corrupted = DiffPath.Managed | DiffPath.Native | DiffPath.Cuda
    )
    {
        // Set by the path runner before each launch: whether this launch is on a corrupted path
        var corruptThisLaunch = new StrongBox<bool>();
        var runners = new[] { DiffPath.Managed, DiffPath.Native, DiffPath.Cuda }
            .Where(p => (paths & p) != 0)
            .Select(p => new DiffPathRunner(p, launch =>
            {
                corruptThisLaunch.Value = (corrupted & p) != 0;
                launch();
            }))
            .ToList();

        var testCase = new DifferentialCase(FakeCaseName)
        {
            Shape = FakeShape,
            Inputs = static (rng, rows) =>
            {
                var (a, b) = DiffValues.Int32Pairs(rng, rows);
                return new DiffBuffers().In("a", a).In("b", b).Out<int>("r", rows, s_fakeColumns, OutputPolicy.Exact);
            },
            Launch = (buffers, shape) =>
            {
                var a = buffers.Get<int>("a");
                var b = buffers.Get<int>("b");
                var r = buffers.Get<int>("r");
                for (int t = 0; t < shape.Rows; t++)
                {
                    r[t * 2] = unchecked(a[t] + b[t]);
                    r[(t * 2) + 1] = a[t] ^ b[t];
                }

                if (corruptThisLaunch.Value)
                {
                    corrupt?.Invoke(r);
                }
            },
            Oracle = static (buffers, shape) => HarnessProbeKernels.LaunchInt32AddXorKernel(
                buffers.Get<int>("a"),
                buffers.Get<int>("b"),
                buffers.Get<int>("r"),
                gridDimX: shape.GridX,
                gridDimY: shape.GridY,
                blockDimX: shape.BlockX,
                blockDimY: shape.BlockY
            ),
        };
        return new FakeRun(testCase, runners);
    }

    /// <summary>A fake case with the host paths that run it.</summary>
    public sealed record FakeRun(DifferentialCase Case, IReadOnlyList<DiffPathRunner> Paths)
    {
        public DifferentialReport Evaluate(IReadOnlyList<int> seeds, IReadOnlyList<KnownDivergence>? entries = null) =>
            DifferentialRunner.Evaluate(Case, seeds, Paths, entries ?? []);
    }
}
