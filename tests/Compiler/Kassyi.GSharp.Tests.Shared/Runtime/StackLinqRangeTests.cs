using System;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Two position stages of a fused StackLinq chain give the range of C# (#624 R1, #677), and the argument of a stage is
/// evaluated once.
/// </summary>
/// <remarks>
/// Properties: (1) the device sum equals the host StackLinq sum of the same chain, which (2) equals the System.Linq sum,
/// for the arguments -1, 0, 3, the length and the length + 1; (3) every thread, sequential and AsParallel(), agrees.
/// </remarks>
[Collection(TestCollections.GlobalStateSequential)]
public class StackLinqRangeTests : TranspilerTestBase
{
    private const uint GridDimX = 2;
    private const uint BlockDimX = 64;
    private static readonly int[] s_data = Enumerable.Range(1, 10).ToArray();
    private static readonly int[] s_arguments = [-1, 0, 3, 10, 11];

    private static readonly (string First, string Second)[] s_combos =
    [
        ("Skip", "Skip"), ("Skip", "Take"), ("Take", "Skip"), ("Take", "Take"),
        ("SkipLast", "Skip"), ("SkipLast", "Take"), ("TakeLast", "Skip"), ("TakeLast", "Take"),
        ("SkipLast", "SkipLast"), ("SkipLast", "TakeLast"), ("TakeLast", "SkipLast"), ("TakeLast", "TakeLast"),
    ];

    public static TheoryData<int, bool> Cases()
    {
        var data = new TheoryData<int, bool>();
        foreach (var managed in new[] { false, true })
        {
            for (var combo = 0; combo < s_combos.Length; combo++)
            {
                data.Add(combo, managed);
            }
        }

        return data;
    }

    public static TheoryData<bool> Paths() => new() { false, true };

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_TwoStageRange_MatchesCSharp(int combo, bool managed)
    {
        Skip.If(managed && IsCudaDialect, "The managed path runs on the CPU test assembly.");
        foreach (var a in s_arguments)
        {
            foreach (var b in s_arguments)
            {
                var result = new int[GridDimX * BlockDimX * 2];
                Run(managed, () => StackLinqRangeKernels.LaunchTwoStageRangeKernel(s_data, combo, a, b, result,
                    gridDimX: GridDimX, blockDimX: BlockDimX));

                var (first, second) = s_combos[combo];
                var expected = HostStackLinqSum(combo, a, b);

                // (2) the host StackLinq agrees with System.Linq, so the expectation is the C# meaning
                Assert.Equal(SystemLinqSum(combo, a, b), expected);

                // (1) and (3)
                // mutant: IntermediateStrategies SkipLast compared with the original length (the #677 form) fails SkipLast.TakeLast
                for (var i = 0; i < result.Length; i++)
                {
                    Assert.True(result[i] == expected,
                        $"{first}({a}).{second}({b}): slot {i} ({(i % 2 == 0 ? "sequential" : "AsParallel")}) is {result[i]}, expected {expected}");
                }
            }
        }
    }

    // C# evaluates the argument of Take / Skip once, when the chain is built; a position stage after Where still counts
    // the elements that pass
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_StageArguments_AreEvaluatedOnce(bool managed)
    {
        int[] counter = [0];
        int[] result = [0, 0];

        Run(managed, () => StackLinqRangeKernels.LaunchFilteredTakeKernel(s_data, counter, result, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: the old per-element evaluation of Take(n) / Skip(n) (the argument written inside the loop)
        Assert.Equal(2, counter[0]);
        Assert.Equal(2 + 4 + 6, result[0]);
        Assert.Equal(s_data.Skip(3).Sum() * 10, result[1]);
    }

    // Second review (M1): C# calls a side-effecting selector for the elements that a following Skip drops; the Skip stays
    // in the loop instead of narrowing the range
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SideEffectingSelectThenSkip_CallsTheSelectorForEveryElement(bool managed)
    {
        int[] counter = [0];
        int[] result = [0];

        Run(managed, () => StackLinqRangeKernels.LaunchSelectSkipCountKernel(s_data, counter, result, gridDimX: GridDimX, blockDimX: BlockDimX));

        // mutant: StackLinqChainExtractor.CountRangeStages without the stop after a selector that writes (7 calls)
        Assert.Equal(s_data.Length, counter[0]);
        Assert.Equal(s_data.Skip(3).Sum(), result[0]);
    }

    // Second review (M2): every three-stage chain, with seeded lengths 1 to 24 and arguments at the edges (-1, 0,
    // int.MinValue, int.MaxValue, the length), on 33 threads (a partial warp), sequential and AsParallel()
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ThreeStageChains_SeededSweep_MatchCSharp(bool managed)
    {
        const int Threads = 33;
        const int Chains = StackLinqRangeKernels.ThreeStageChains;
        const int Seed = 20261028;
        var random = new Random(Seed);
        var failures = new List<string>();
        for (var trial = 0; trial < 150; trial++)
        {
            var n = random.Next(1, 25);
            var data = new int[n];
            for (var i = 0; i < n; i++)
            {
                data[i] = random.Next(-1000, 1000);
            }

            int Argument() => random.Next(8) switch
            {
                0 => -1,
                1 => 0,
                2 => int.MaxValue,
                3 => int.MinValue,
                4 => n,
                _ => random.Next(0, n + 3),
            };

            var (a, b, c) = (Argument(), Argument(), Argument());
            var result = new int[Threads * Chains * 2];
            Run(managed, () => StackLinqRangeKernels.LaunchThreeStageSweepKernel(data, a, b, c, result, gridDimX: 1, blockDimX: Threads));
            for (var chain = 0; chain < Chains; chain++)
            {
                var expected = ThreeStageHostSum(chain, data, a, b, c);
                for (var slot = 0; slot < Threads * 2; slot++)
                {
                    var got = result[slot / 2 * Chains * 2 + chain * 2 + slot % 2];
                    if (got != expected && failures.Count < 20)
                    {
                        failures.Add($"seed {Seed} trial {trial} n={n} {s_threeStageNames[chain]}({a},{b},{c}) thread {slot / 2} {(slot % 2 == 0 ? "sequential" : "AsParallel")}: {got} != {expected}");
                    }
                }
            }
        }

        // mutant: StackLinqFusionRule TakeLast range stage `lo = hi - n` -> `lo = lo + n`
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static int ThreeStageHostSum(int chain, int[] data, int a, int b, int c) => chain switch
    {
        0 => data.AsStackEnumerable().Skip(a).Skip(b).Skip(c).Sum(),
        1 => data.AsStackEnumerable().Skip(a).Skip(b).Take(c).Sum(),
        2 => data.AsStackEnumerable().Skip(a).Take(b).Skip(c).Sum(),
        3 => data.AsStackEnumerable().Skip(a).Take(b).Take(c).Sum(),
        4 => data.AsStackEnumerable().Take(a).Skip(b).Skip(c).Sum(),
        5 => data.AsStackEnumerable().Take(a).Skip(b).Take(c).Sum(),
        6 => data.AsStackEnumerable().Take(a).Take(b).Skip(c).Sum(),
        7 => data.AsStackEnumerable().Take(a).Take(b).Take(c).Sum(),
        8 => data.AsStackEnumerable().SkipLast(a).Skip(b).Skip(c).Sum(),
        9 => data.AsStackEnumerable().SkipLast(a).Skip(b).Take(c).Sum(),
        10 => data.AsStackEnumerable().SkipLast(a).Take(b).Skip(c).Sum(),
        11 => data.AsStackEnumerable().SkipLast(a).Take(b).Take(c).Sum(),
        12 => data.AsStackEnumerable().SkipLast(a).SkipLast(b).Skip(c).Sum(),
        13 => data.AsStackEnumerable().SkipLast(a).SkipLast(b).Take(c).Sum(),
        14 => data.AsStackEnumerable().SkipLast(a).SkipLast(b).SkipLast(c).Sum(),
        15 => data.AsStackEnumerable().SkipLast(a).SkipLast(b).TakeLast(c).Sum(),
        16 => data.AsStackEnumerable().SkipLast(a).TakeLast(b).Skip(c).Sum(),
        17 => data.AsStackEnumerable().SkipLast(a).TakeLast(b).Take(c).Sum(),
        18 => data.AsStackEnumerable().SkipLast(a).TakeLast(b).SkipLast(c).Sum(),
        19 => data.AsStackEnumerable().SkipLast(a).TakeLast(b).TakeLast(c).Sum(),
        20 => data.AsStackEnumerable().TakeLast(a).Skip(b).Skip(c).Sum(),
        21 => data.AsStackEnumerable().TakeLast(a).Skip(b).Take(c).Sum(),
        22 => data.AsStackEnumerable().TakeLast(a).Take(b).Skip(c).Sum(),
        23 => data.AsStackEnumerable().TakeLast(a).Take(b).Take(c).Sum(),
        24 => data.AsStackEnumerable().TakeLast(a).SkipLast(b).Skip(c).Sum(),
        25 => data.AsStackEnumerable().TakeLast(a).SkipLast(b).Take(c).Sum(),
        26 => data.AsStackEnumerable().TakeLast(a).SkipLast(b).SkipLast(c).Sum(),
        27 => data.AsStackEnumerable().TakeLast(a).SkipLast(b).TakeLast(c).Sum(),
        28 => data.AsStackEnumerable().TakeLast(a).TakeLast(b).Skip(c).Sum(),
        29 => data.AsStackEnumerable().TakeLast(a).TakeLast(b).Take(c).Sum(),
        30 => data.AsStackEnumerable().TakeLast(a).TakeLast(b).SkipLast(c).Sum(),
        31 => data.AsStackEnumerable().TakeLast(a).TakeLast(b).TakeLast(c).Sum(),
        _ => throw new ArgumentOutOfRangeException(nameof(chain)),
    };

    private static readonly string[] s_threeStageNames = ["Skip.Skip.Skip", "Skip.Skip.Take", "Skip.Take.Skip", "Skip.Take.Take", "Take.Skip.Skip", "Take.Skip.Take", "Take.Take.Skip", "Take.Take.Take", "SkipLast.Skip.Skip", "SkipLast.Skip.Take", "SkipLast.Take.Skip", "SkipLast.Take.Take", "SkipLast.SkipLast.Skip", "SkipLast.SkipLast.Take", "SkipLast.SkipLast.SkipLast", "SkipLast.SkipLast.TakeLast", "SkipLast.TakeLast.Skip", "SkipLast.TakeLast.Take", "SkipLast.TakeLast.SkipLast", "SkipLast.TakeLast.TakeLast", "TakeLast.Skip.Skip", "TakeLast.Skip.Take", "TakeLast.Take.Skip", "TakeLast.Take.Take", "TakeLast.SkipLast.Skip", "TakeLast.SkipLast.Take", "TakeLast.SkipLast.SkipLast", "TakeLast.SkipLast.TakeLast", "TakeLast.TakeLast.Skip", "TakeLast.TakeLast.Take", "TakeLast.TakeLast.SkipLast", "TakeLast.TakeLast.TakeLast"];

    // Re-review (R2): Take(n) after a selector or predicate that runs in the loop calls it as C# does: n times, up to the
    // n-th passing element, and not at all for n <= 0
    [SkippableTheory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(10, false)]
    [InlineData(11, false)]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_TakeAfterLoopStage_CallsTheStageAsCSharp(int take, bool managed)
    {
        int[] counter = [0, 0];
        int[] result = [0, 0];

        Run(managed, () => StackLinqRangeKernels.LaunchTakeCountKernel(s_data, counter, take, result, gridDimX: GridDimX, blockDimX: BlockDimX));

        // The expected counts come from the same chains on the host (StackLinq, C#)
        int[] hostCounter = [0, 0];
        var hostSelect = s_data.AsStackEnumerable().Select(x => System.Threading.Interlocked.Add(ref hostCounter[0], 1) * 0 + x).Take(take).Sum();
        var hostWhere = s_data.AsStackEnumerable().Where(x => System.Threading.Interlocked.Add(ref hostCounter[1], 1) > 0 && x % 2 == 0).Take(take).Sum();

        // mutant: StackLinqFusionRule without the stop of the Take at the head of the loop body (no stop, or n + 1
        // calls when it is checked at the Take)
        Assert.Equal(hostCounter, counter);
        Assert.Equal([hostSelect, hostWhere], result);
    }

    // Third review: Select(f).Take(a).Skip(b) / SkipWhile and Where(p).Take(a).Skip(b) call f and p as C# does, also when the
    // Skip passes over the a-th element (Select(f).Take(1).Skip(1): once, not twice; Where(p).Take(2).Skip(2): twice, not
    // four times)
    [SkippableTheory]
    [InlineData(1, 1, false)]
    [InlineData(2, 2, false)]
    [InlineData(0, 1, false)]
    [InlineData(3, 1, false)]
    [InlineData(10, 3, false)]
    [InlineData(11, 12, false)]
    [InlineData(1, 1, true)]
    [InlineData(2, 2, true)]
    [InlineData(0, 1, true)]
    [InlineData(3, 1, true)]
    [InlineData(10, 3, true)]
    [InlineData(11, 12, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_TakeThenSkip_CallsTheStageAsCSharp(int take, int skip, bool managed)
    {
        int[] counter = [0, 0, 0];
        int[] result = [0, 0, 0];

        Run(managed, () => StackLinqRangeKernels.LaunchTakeSkipCountKernel(s_data, counter, take, skip, result, gridDimX: GridDimX, blockDimX: BlockDimX));

        // The expected counts come from the same chains on the host (StackLinq, C#)
        int[] hostCounter = [0, 0, 0];
        int[] hostResult =
        [
            s_data.AsStackEnumerable().Select(x => System.Threading.Interlocked.Add(ref hostCounter[0], 1) * 0 + x).Take(take).Skip(skip).Sum(),
            s_data.AsStackEnumerable().Where(x => System.Threading.Interlocked.Add(ref hostCounter[1], 1) > 0 && x % 2 == 0).Take(take).Skip(skip).Sum(),
            s_data.AsStackEnumerable().Select(x => System.Threading.Interlocked.Add(ref hostCounter[2], 1) * 0 + x).Take(take).SkipWhile(x => x <= skip).Sum(),
        ];

        // mutant: StackLinqFusionRule checks the stop of the Take after the terminal instead of at the head of the loop body
        // (a Skip's continue passes it: twice for Take(1).Skip(1))
        Assert.Equal(hostCounter, counter);
        Assert.Equal(hostResult, result);
    }

    private void Run(bool managed, Action launch)
    {
        Skip.If(managed && IsCudaDialect, "The managed path runs on the CPU test assembly.");
#if GSHARP_CPU
        if (managed)
        {
            ManagedKernelRun.Run(launch);
            return;
        }
#endif
        launch();
    }

    private static int HostStackLinqSum(int combo, int a, int b) => combo switch
    {
        0 => s_data.AsStackEnumerable().Skip(a).Skip(b).Sum(),
        1 => s_data.AsStackEnumerable().Skip(a).Take(b).Sum(),
        2 => s_data.AsStackEnumerable().Take(a).Skip(b).Sum(),
        3 => s_data.AsStackEnumerable().Take(a).Take(b).Sum(),
        4 => s_data.AsStackEnumerable().SkipLast(a).Skip(b).Sum(),
        5 => s_data.AsStackEnumerable().SkipLast(a).Take(b).Sum(),
        6 => s_data.AsStackEnumerable().TakeLast(a).Skip(b).Sum(),
        7 => s_data.AsStackEnumerable().TakeLast(a).Take(b).Sum(),
        8 => s_data.AsStackEnumerable().SkipLast(a).SkipLast(b).Sum(),
        9 => s_data.AsStackEnumerable().SkipLast(a).TakeLast(b).Sum(),
        10 => s_data.AsStackEnumerable().TakeLast(a).SkipLast(b).Sum(),
        _ => s_data.AsStackEnumerable().TakeLast(a).TakeLast(b).Sum(),
    };

    private static int SystemLinqSum(int combo, int a, int b)
    {
        IEnumerable<int> data = s_data;
        var (first, second) = s_combos[combo];
        return Apply(Apply(data, first, a), second, b).Sum();
    }

    private static IEnumerable<int> Apply(IEnumerable<int> source, string stage, int n) => stage switch
    {
        "Skip" => source.Skip(n),
        "Take" => source.Take(n),
        "SkipLast" => source.SkipLast(n),
        _ => source.TakeLast(n),
    };
}
