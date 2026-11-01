#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Analysis;

/// <summary>
/// Verifies the table of <see cref="SyncIntrinsicClassifier"/> (#503): each synchronization intrinsic of
/// <c>Kassyi.GSharp.Core</c> by symbol, user types of the same names, and every public static method of the Core
/// <c>Gpu</c>, <c>Warp</c> and <c>Wmma</c> types, so that a new intrinsic is classified when it is added.
/// </summary>
[Category(TestCategory.Compiler)]
public class SyncIntrinsicClassifierTests
{
    private const string CoreNamespace = "Kassyi.GSharp.Core";

    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analysis/SyncIntrinsicClassifierCases.cs")
    );

    // Every Core member that the classifier gives something other than the default: (type, name) -> expected
    private static readonly Dictionary<(string Type, string Name), SyncIntrinsicInfo> s_classified = new()
    {
        [("Gpu", "SyncThreads")] = new(SyncIntrinsicKind.BlockBarrier, SyncScope.Block, true, false),
        [("Gpu", "SyncGrid")] = new(SyncIntrinsicKind.GridBarrier, SyncScope.Grid, true, false),
        [("Gpu", "SyncWarp")] = new(SyncIntrinsicKind.WarpBarrier, SyncScope.Warp, false, false),
        [("Gpu", "MatchAnySync")] = new(SyncIntrinsicKind.WarpShuffle, SyncScope.Warp, true, true, WarpShuffleKind.MatchAny),
        [("Gpu", "MatchAllSync")] = new(SyncIntrinsicKind.WarpShuffle, SyncScope.Warp, true, true, WarpShuffleKind.MatchAll),
        [("Gpu", "ThreadFence")] = new(SyncIntrinsicKind.MemoryFence, SyncScope.None, false, false),
        [("Gpu", "ThreadFenceBlock")] = new(SyncIntrinsicKind.MemoryFence, SyncScope.None, false, false),
        [("Gpu", "ThreadFenceSystem")] = new(SyncIntrinsicKind.MemoryFence, SyncScope.None, false, false),
        [("Gpu", "MBarrierInit")] = new(SyncIntrinsicKind.AsyncBarrier, SyncScope.None, false, false),
        [("Gpu", "MBarrierArrive")] = new(SyncIntrinsicKind.AsyncBarrier, SyncScope.None, false, false),
        [("Gpu", "MBarrierWait")] = new(SyncIntrinsicKind.AsyncBarrier, SyncScope.None, false, false),
        [("Gpu", "LaneId")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "WarpId")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "SmId")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "Clock")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "Clock64")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "AtomicAdd")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "AtomicSub")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "AtomicExch")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "AtomicMin")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "AtomicMax")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "AtomicInc")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "AtomicDec")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "AtomicCAS")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "AtomicAnd")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "AtomicOr")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Gpu", "AtomicXor")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Warp", "ShuffleSync")] = new(SyncIntrinsicKind.WarpShuffle, SyncScope.Warp, true, true, WarpShuffleKind.Sync),
        [("Warp", "ShuffleDownSync")] = new(SyncIntrinsicKind.WarpShuffle, SyncScope.Warp, true, true, WarpShuffleKind.Down),
        [("Warp", "ShuffleUpSync")] = new(SyncIntrinsicKind.WarpShuffle, SyncScope.Warp, true, true, WarpShuffleKind.Up),
        [("Warp", "ShuffleXorSync")] = new(SyncIntrinsicKind.WarpShuffle, SyncScope.Warp, true, true, WarpShuffleKind.Xor),
        [("Warp", "BallotSync")] = new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, false, true),
        [("Warp", "AllSync")] = new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, false, true),
        [("Warp", "AnySync")] = new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, false, true),
        [("Warp", "ReduceAddSync")] = new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, false, true),
        [("Warp", "ReduceMinSync")] = new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, false, true),
        [("Warp", "ReduceMaxSync")] = new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, false, true),
        [("Warp", "ReduceAndSync")] = new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, false, true),
        [("Warp", "ReduceOrSync")] = new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, false, true),
        [("Warp", "ReduceXorSync")] = new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, false, true),
        [("Warp", "ActiveMask")] = new(SyncIntrinsicKind.None, SyncScope.None, false, true),
        [("Wmma", "Fill")] = new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, false, true),
        [("Wmma", "Load")] = new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, false, true),
        [("Wmma", "MultiplyAccumulate")] = new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, false, true),
        [("Wmma", "Store")] = new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, false, true),
    };

    // The Core Gpu members that neither synchronize nor return a thread-variant value (the classifier's default)
    private static readonly HashSet<string> s_plainGpuMembers =
    [
        "AbsDiff", "AddWithCarry", "ApplyPriorityL2", "BitFieldExtract", "BitFieldInsert", "BitReverse", "BitReverse64",
        "BytePermute", "Constant", "ConvertToSharedMemory", "CopyAsync", "CopyAsyncCommitGroup", "CopyAsyncWaitAll",
        "CopyAsyncWaitGroup", "CountLeadingZeros", "CountTrailingZeros", "DotProduct4", "ForUnroll", "FunnelShiftLeft",
        "FunnelShiftRight", "GridStrideLoop", "InlinePtx", "LoadCachedGlobal", "Lop3", "MultiplyAddWide",
        "MultiplyAddWideWithCarry", "MultiplyHigh", "MultiplyWide", "NanoSleep", "PopCount", "Prefetch",
        "PrefetchUniform", "ReductionAdd", "Sad4", "SingleThread", "StoreCachedGlobal", "SubWithBorrow"
    ];

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Classify_EveryCoreMember_MatchesTheTable()
    {
        var unlisted = new List<string>();
        var mismatched = new List<string>();
        var seen = new HashSet<(string, string)>();
        foreach (var typeName in new[] { "Gpu", "Warp", "Wmma" })
        {
            foreach (var method in GetCoreType(typeName).GetMembers().OfType<IMethodSymbol>()
                         .Where(static m => m is { IsStatic: true, DeclaredAccessibility: Accessibility.Public, MethodKind: MethodKind.Ordinary }))
            {
                var key = (typeName, method.Name);
                seen.Add(key);
                SyncIntrinsicInfo expected;
                if (s_classified.TryGetValue(key, out var listed))
                {
                    expected = listed;
                }
                else if (typeName == "Gpu" && s_plainGpuMembers.Contains(method.Name))
                {
                    expected = default;
                }
                else
                {
                    unlisted.Add($"{typeName}.{method.Name}");
                    continue;
                }

                var actual = SyncIntrinsicClassifier.Classify(method);
                if (!Equal(expected, actual))
                {
                    mismatched.Add($"{typeName}.{method.Name}: expected {Format(expected)}, got {Format(actual)}");
                }
            }
        }

        var missing = s_classified.Keys
            .Concat(s_plainGpuMembers.Select(static name => ("Gpu", name)))
            .Where(key => !seen.Contains(key))
            .Select(static key => $"{key.Item1}.{key.Item2}")
            .ToList();

        Assert.True(unlisted.Count == 0,
            "Core members that the table does not classify (add them to SyncIntrinsicClassifier or to the plain members): " +
            string.Join(", ", unlisted.Distinct()));
        Assert.True(mismatched.Count == 0, string.Join(Environment.NewLine, mismatched.Distinct()));
        Assert.True(missing.Count == 0, "Listed members that Core does not declare: " + string.Join(", ", missing));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Gpu", "SyncThreads", nameof(SyncIntrinsicKind.BlockBarrier))]
    [InlineData("Gpu", "SyncGrid", nameof(SyncIntrinsicKind.GridBarrier))]
    [InlineData("Warp", "ShuffleDownSync", nameof(SyncIntrinsicKind.WarpShuffle))]
    public void Test_Classify_EveryOverload_SharesTheClassification(string typeName, string name, string kind)
    {
        var overloads = GetCoreType(typeName).GetMembers(name).OfType<IMethodSymbol>().ToList();

        Assert.NotEmpty(overloads);
        Assert.All(overloads, method => Assert.Equal(kind, SyncIntrinsicClassifier.Classify(method).Kind.ToString()));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Classify_SyncGridWithBody_SplitsTheStateMachine()
    {
        var withBody = GetCoreType("Gpu").GetMembers("SyncGrid").OfType<IMethodSymbol>()
            .Single(static method => method.Parameters.Length == 1);

        Assert.True(SyncIntrinsicClassifier.Classify(withBody).SplitsStateMachine);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("SyncIntrinsicCases.Gpu", "SyncThreads")]
    [InlineData("SyncIntrinsicCases.Gpu", "SyncGrid")]
    [InlineData("SyncIntrinsicCases.Gpu", "LaneId")]
    [InlineData("SyncIntrinsicCases.Warp", "ShuffleDownSync")]
    public void Test_Classify_UserTypeOfTheSameName_IsNotAnIntrinsic(string typeName, string name)
    {
        var method = GetType(typeName).GetMembers(name).OfType<IMethodSymbol>().Single();

        Assert.True(Equal(default, SyncIntrinsicClassifier.Classify(method)));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Classify_GpuHintUnroll_IsNotClassified()
    {
        var hint = GetCoreType("Gpu").GetTypeMembers("Hint").Single();
        var unroll = hint.GetMembers("Unroll").OfType<IMethodSymbol>().Single();

        Assert.True(Equal(default, SyncIntrinsicClassifier.Classify(unroll)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_IsThreadVariantSource_ThreadIndexMembers()
    {
        var threadIndex = GetCoreType("ThreadIndex");

        Assert.All(new[] { "X", "Y", "Z" },
            name => Assert.True(SyncIntrinsicClassifier.IsThreadVariantSource(threadIndex.GetMembers(name).Single())));
        Assert.False(SyncIntrinsicClassifier.IsThreadVariantSource(GetCoreType("BlockIndex").GetMembers("X").Single()));
        Assert.False(SyncIntrinsicClassifier.IsThreadVariantSource(GetType("SyncIntrinsicCases.ThreadIndex").GetMembers("X").Single()));
    }

    private static INamedTypeSymbol GetCoreType(string name) => GetType($"{CoreNamespace}.{name}");

    private static INamedTypeSymbol GetType(string metadataName) =>
        s_compilation.GetTypeByMetadataName(metadataName) ??
        throw new InvalidOperationException($"Type '{metadataName}' was not found in the test compilation.");

    private static bool Equal(SyncIntrinsicInfo expected, SyncIntrinsicInfo actual) =>
        expected.Kind == actual.Kind &&
        expected.Scope == actual.Scope &&
        expected.SplitsStateMachine == actual.SplitsStateMachine &&
        expected.ResultIsThreadVariant == actual.ResultIsThreadVariant &&
        expected.Shuffle == actual.Shuffle;

    private static string Format(SyncIntrinsicInfo info) =>
        $"({info.Kind}, {info.Scope}, splits: {info.SplitsStateMachine}, variant: {info.ResultIsThreadVariant}, {info.Shuffle})";
}
