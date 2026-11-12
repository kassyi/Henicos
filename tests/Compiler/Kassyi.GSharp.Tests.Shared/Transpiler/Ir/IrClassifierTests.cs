#nullable enable
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Compiler.Lowering;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies the classify pass of the IR (#1561): the split of each call, what it may write, that the tree is rebuilt
/// only where it changes, and that a call nobody classified cannot be read.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrClassifierTests
{
    private const string Cases = "Ir/IrClassifierCases.cs";

    private static Compilation Compilation => IrTestCases.Compile(Cases);

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("BlockBarrier", "BlockBarrier", "None")]
    [InlineData("GridBarrier", "GridBarrier", "None")]
    [InlineData("SyncWarp", "WarpShuffle", "SyncWarp")]
    [InlineData("ShuffleDown", "WarpShuffle", "Down")]
    [InlineData("ActiveMask", "WarpShuffle", "ActiveMask")]
    [InlineData("Fence", "None", "None")]
    [InlineData("CallsSuspendingValue", "SuspendingCall", "None")]
    [InlineData("CallsSuspendingVoid", "SuspendingCall", "None")]
    [InlineData("CallsPure", "None", "None")]
    [InlineData("ScalarMath", "None", "None")]
    public void Classify_SingleCall_RecordsItsSplit(string name, string kind, string shuffle)
    {
        var call = Assert.Single(Calls(Classify(name)));

        Assert.Equal(new IrSplit(Enum.Parse<IrSplitKind>(kind), Enum.Parse<WarpShuffleKind>(shuffle)),
            call.Classification.Split);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("CallsPure", "None")]
    [InlineData("CallsSetRef", "RefParameters")]
    [InlineData("CallsWriteArray", "Memory")]
    [InlineData("ScalarMath", "None")]
    public void Classify_SingleCall_RecordsWhatItWrites(string name, string effect)
    {
        var call = Assert.Single(Calls(Classify(name)));

        Assert.Equal(Enum.Parse<DeviceFunctionEffect>(effect), call.Classification.Effect);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Classify_NestedCalls_ClassifiesEachOne()
    {
        var calls = Calls(Classify("NestedSplit"));

        Assert.Equal(
            ["Pure:None", "SuspendingValue:SuspendingCall", "ShuffleXorSync:WarpShuffle"],
            calls.Select(static call => call.Method.Name + ":" + call.Classification.Split.Kind));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Classify_WithoutCalls_ReturnsTheSameTree()
    {
        var built = Build("NoCall");

        Assert.Same(built, IrClassifier.Classify(built, Compilation));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Classify_WithACall_RebuildsOnlyThePathToTheCall()
    {
        var built = Build("NestedSplit");
        var classified = IrClassifier.Classify(built, Compilation);
        var builtBinary = (IrBinary)IrTestCases.Result(built);
        var classifiedBinary = (IrBinary)IrTestCases.Result(classified);
        var builtShuffle = (IrCall)builtBinary.Right;
        var classifiedShuffle = (IrCall)classifiedBinary.Right;

        Assert.NotSame(built, classified);
        Assert.NotSame(builtShuffle, classifiedShuffle);

        // The arguments of the shuffle hold no call, so they stay the same nodes
        Assert.Same(builtShuffle.Arguments[1].Value, classifiedShuffle.Arguments[1].Value);
        Assert.Same(built.Body.Locals[0], classified.Body.Locals[0]);
        Assert.Empty(IrInvariants.Verify(classified, Compilation));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("BarrierInLoop", "HoldsSplit")]
    [InlineData("FenceInLoop", "None")]
    public void Classify_OpaqueLoop_RecordsWhetherItHoldsASplit(string name, string split)
    {
        var loop = Assert.IsType<IrOpaqueStatement>(Classify(name).Body.Statements[0]);

        Assert.Equal(Enum.Parse<IrOpaqueSplit>(split), loop.Split);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Classify_EveryCallAndOpaqueNode_KeepsTheClassifiedInvariants()
    {
        Assert.NotEmpty(IrInvariants.VerifyClassified(Build("BarrierInLoop")));
        Assert.Empty(IrInvariants.VerifyClassified(Classify("BarrierInLoop")));
        Assert.Contains(IrInvariants.VerifyClassified(Build("NestedSplit")),
            static violation => violation.StartsWith("I24 ", StringComparison.Ordinal));
        Assert.Contains(IrInvariants.VerifyClassified(Build("BarrierInLoop")),
            static violation => violation.StartsWith("I25 ", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Print_ClassifiedCall_ShowsItsSplitAndEffect()
    {
        var printed = IrPrinter.Print(IrTestCases.Result(Classify("ShuffleDown")));

        Assert.Contains("[split=WarpShuffle:Down, writes=Memory]", printed);
        Assert.DoesNotContain("[split=", IrPrinter.Print(IrTestCases.Result(Build("ShuffleDown"))));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Split_OfAnUnclassifiedCall_Throws()
    {
        var call = Assert.Single(Calls(Build("BlockBarrier")));

        Assert.False(call.Classification.IsClassified);
        Assert.Throws<InvalidOperationException>(() => call.Classification.Split);
        Assert.Throws<InvalidOperationException>(() => call.Classification.Effect);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Effects_OfAnUnclassifiedCall_Throw()
    {
        var call = Assert.Single(Calls(Build("CallsPure")));

        Assert.Throws<InvalidOperationException>(() => IrEffects.Of(call, Compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void WarpShuffle_WithoutAnOperation_IsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => IrSplit.WarpShuffle(WarpShuffleKind.None));

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SplitClassifier_EveryCoreIntrinsic_MatchesTheLoweringAnnotation()
    {
        var compilation = Compilation;
        var methods = new[] { "Kassyi.GSharp.Core.Gpu", "Kassyi.GSharp.Core.Warp", "Kassyi.GSharp.Core.Wmma",
                "System.Threading.Interlocked" }
            .Select(name => compilation.GetTypeByMetadataName(name) ??
                            throw new InvalidOperationException($"Type '{name}' not found."))
            .SelectMany(static type => type.GetMembers().OfType<IMethodSymbol>())
            .Where(static method => method is { IsStatic: true, MethodKind: MethodKind.Ordinary })
            .ToArray();

        var mismatches = methods
            .Where(method => !Matches(IrSplitClassifier.Classify(method, compilation),
                AstLoweringPipeline.ClassifySplitPoint(method, compilation), method.ReturnsVoid))
            .Select(static method => method.ContainingType.Name + "." + method.Name)
            .ToArray();

        Assert.True(methods.Length > 40, $"Only {methods.Length} intrinsics.");
        Assert.Empty(mismatches);
    }

    // The annotation the compiler maps the split to equals the one the lowering puts on the call
    private static bool Matches(IrSplit split, SyntaxAnnotation? annotation, bool returnsVoid) =>
        LoweringAnnotations.CreateSplitAnnotation(split, returnsVoid) is var mapped &&
        mapped?.Kind == annotation?.Kind && mapped?.Data == annotation?.Data;

    internal static IrMethod Build(string name) => IrTestCases.Build(Cases, "IrClassifierCases.Cases", name);

    internal static IrMethod Classify(string name) => IrClassifier.Classify(Build(name), Compilation);

    internal static List<IrCall> Calls(IrMethod method) =>
        IrNodeChildren.DescendantsAndSelf(method.Body).OfType<IrCall>().ToList();

    // The statement of a body with one statement, for the kernels written as expression bodies
    internal static IrExpression Single(IrMethod method) =>
        Assert.IsType<IrExpressionStatement>(Assert.Single(method.Body.Statements)).Expression;
}
