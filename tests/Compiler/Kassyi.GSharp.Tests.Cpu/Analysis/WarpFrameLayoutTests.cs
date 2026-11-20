#nullable enable

using Kassyi.GSharp.Compiler.Lowering;

namespace Kassyi.GSharp.Tests.Shared.Analysis;

[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
public class WarpFrameLayoutTests
{
    private const string CasesType = "WarpFrameLayoutCases.Kernels";

    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        File.ReadAllText(RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", "Analysis", "Cases", "WarpFrameLayoutCases.cs"))
    );

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Layout_NestedCalls_MatchesThePreorderTable()
    {
        var kernel = GetLayout("NestedKernel");
        var twice = GetLayout("HalfThenFullTwice");
        var halfThenFull = GetLayout("HalfThenFull");

        Assert.Equal((8, 0), (kernel.Size, kernel.CollectiveCount));
        Assert.Equal((7, 0), (twice.Size, twice.CollectiveCount));
        Assert.Equal((3, 2), (halfThenFull.Size, halfThenFull.CollectiveCount));

        var paths = WalkStaticPaths("NestedKernel");

        Assert.Equal(
            [("NestedKernel", 0), ("HalfThenFullTwice", 1), ("HalfThenFull", 2), ("HalfThenFull", 5)],
            paths.Frames);
        Assert.Equal([3, 4, 6, 7], paths.Counts);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Layout_TwoCallSitesOfOneFunction_GetDifferentBases()
    {
        Assert.Equal([1, 3], CallOffsets("TwoCallSitesKernel"));
        Assert.Equal([1, 4], CallOffsets("HalfThenFullTwice"));

        var paths = WalkStaticPaths("TwoCallSitesKernel");

        Assert.Equal([("TwoCallSitesKernel", 0), ("FullPairSum", 1), ("FullPairSum", 3)], paths.Frames);
        Assert.Equal([2, 4], paths.Counts);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Layout_EveryStaticPath_GetsADistinctBase()
    {
        var kernel = GetLayout("EveryPathKernel");
        var paths = WalkStaticPaths("EveryPathKernel");
        var bases = paths.Frames.Select(static f => f.Base).ToList();

        // Relay and RelayTwice have no site of their own: their first callee must still get a base of its own
        Assert.Equal([("Relay", 2), ("FullPairSum", 3)], paths.Frames.Skip(1).Take(2));
        Assert.Equal([("RelayTwice", 5), ("Relay", 6), ("FullPairSum", 7)], paths.Frames.Skip(3).Take(3));
        Assert.Equal(bases.Count, bases.Distinct().Count());
        // Every slot of the kernel's counts is either the head of exactly one frame or exactly one count
        Assert.Equal(Enumerable.Range(0, kernel.Size), bases.Concat(paths.Counts).Order());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Layout_CollectivesOfAFrame_AreNumberedInDocumentOrder()
    {
        var (_, declaration, model) = GetMethod("Mixed");
        var layout = GetLayout("Mixed");
        var sites = Invocations(declaration)
            .Select(call => layout.TryGetSite(call, 0, out var site)
                ? (Name: Callee(call, model).Name, Site: site)
                : default)
            .Where(static s => s.Name is not null)
            .ToList();

        Assert.Equal([("ShuffleDownSync", 0), ("SyncWarp", 1), ("ReduceAddSync", 2)], sites);
        Assert.Equal(1 + 3 + 3, layout.Size);
        Assert.True(layout.TryGetCallOffset(Invocations(declaration).Single(c => Callee(c, model).Name == "Relay"),
            0, out var relay));
        Assert.Equal(4, relay);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Layout_ForUnrollBody_KeepsOneSite()
    {
        var layout = GetLayout("ForUnrollKernel");
        var paths = WalkStaticPaths("ForUnrollKernel");

        Assert.Equal(1, layout.CollectiveCount);
        Assert.Equal(1 + 1 + 2, layout.Size);
        Assert.Equal([("ForUnrollKernel", 0), ("FullPairSum", 2)], paths.Frames);
        Assert.Equal([1, 3], paths.Counts);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("RecursiveKernel")]
    [InlineData("RecursiveShuffle")]
    [InlineData("MutuallyRecursiveKernel")]
    [InlineData("PingShuffle")]
    [InlineData("PongShuffle")]
    public void Layout_RecursiveCallee_Fails(string methodName)
    {
        var (symbol, _, _) = GetMethod(methodName);

        Assert.False(WarpFrameLayout.TryGet(symbol, s_compilation, out _));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Layout_ImportedShuffleCallee_Fails()
    {
        const string LibrarySource = """
            using Kassyi.GSharp.Core;

            namespace ImportedWarpLib;

            public static partial class Helpers
            {
                [GpuDeviceFunction]
                public static int RemotePairSum(int v) => v + Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);
            }
            """;

        const string ConsumerSource = """
            using Kassyi.GSharp.Core;

            namespace ConsumerApp;

            public static partial class Kernels
            {
                [GpuKernel]
                public static void CallRemoteKernel(int[] data)
                {
                    int t = ThreadIndex.X;
                    data[t] = ImportedWarpLib.Helpers.RemotePairSum(data[t]);
                }
            }
            """;

        var libCompilation = RoslynTestUtility.CreateCompilation(LibrarySource).WithAssemblyName("ImportedWarpLib");
        var (_, libOutput, _) = GeneratorTestRunner.RunGenerator(libCompilation, "Cpu");
        var consumer = RoslynTestUtility.CreateCompilation(ConsumerSource)
            .WithAssemblyName("ConsumerApp")
            .AddReferences(libOutput.ToMetadataReference());
        var kernel = consumer.GetTypeByMetadataName("ConsumerApp.Kernels")?.GetMembers("CallRemoteKernel")
            .OfType<IMethodSymbol>().Single() ?? throw new InvalidOperationException("No kernel");

        // The callee is known to reach a shuffle, so the guess of an empty frame would be wrong
        Assert.True(SuspensionAnalysis.ReachesWarpShuffle(kernel, consumer));
        Assert.False(WarpFrameLayout.TryGet(kernel, consumer, out _));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("RecursivePlainKernel")]
    [InlineData("RemovedCallKernel")]
    public void Layout_RecursionOrRemovedCallWithoutReachableShuffle_IsNoFrame(string methodName)
    {
        var paths = WalkStaticPaths(methodName);

        Assert.Equal(3, GetLayout(methodName).Size);
        Assert.Equal([(methodName, 0), ("FullPairSum", 1)], paths.Frames);
        Assert.Equal([2], paths.Counts);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Layout_LoweredCopy_FindsTheSiteByItsOriginalSpan()
    {
        var (_, declaration, _) = GetMethod("FullPairSum");
        var layout = GetLayout("FullPairSum");
        var call = Invocations(declaration).Single();
        // A node of another tree whose own span is unrelated, as a lowered copy is
        var copy = SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Shuffle"));
        var original = copy.WithAdditionalAnnotations(new SyntaxAnnotation(
            LoweringAnnotations.OriginalSpanAnnotationKind,
            LoweringAnnotations.FormatOriginalSpanData(call.SpanStart, call.Span.Length, treeOrdinal: 0)));
        var inlined = copy.WithAdditionalAnnotations(new SyntaxAnnotation(
            LoweringAnnotations.OriginalSpanAnnotationKind,
            LoweringAnnotations.FormatOriginalSpanData(call.SpanStart, call.Span.Length, treeOrdinal: 0, region: 1)));

        Assert.True(layout.TryGetSite(original, 0, out var site));
        Assert.Equal(0, site);
        Assert.False(layout.TryGetSite(inlined, 0, out _));
        Assert.False(layout.TryGetSite(copy, 0, out _));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Layout_KernelCalledAsFunction_Fails()
    {
        var (inner, _, _) = GetMethod("InnerShuffleKernel");
        var (outer, _, _) = GetMethod("KernelCallingKernel");

        // The inner kernel has a layout of its own as a root, but a call of it is no frame of a device function
        Assert.True(WarpFrameLayout.TryGet(inner, s_compilation, out _));
        Assert.False(WarpFrameLayout.TryGet(outer, s_compilation, out _));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Layout_CopyWhoseOwnSpanIsASite_FindsNoSiteOutsideItsOriginalSpan()
    {
        var (_, declaration, _) = GetMethod("FullPairSum");
        var layout = GetLayout("FullPairSum");
        var call = Invocations(declaration).Single();
        Assert.True(layout.TryGetSite(call, 0, out _));

        // The same node in the same tree, so its own span is the span of a real site, but annotated as an inlined copy
        // (region 1) or as a node of another tree: it stands for another call and must not take this site
        foreach (var data in new[]
                 {
                     LoweringAnnotations.FormatOriginalSpanData(call.SpanStart, call.Span.Length, 0, region: 1),
                     LoweringAnnotations.FormatOriginalSpanData(call.SpanStart, call.Span.Length, treeOrdinal: 7),
                 })
        {
            var annotation = new SyntaxAnnotation(LoweringAnnotations.OriginalSpanAnnotationKind, data);
            var root = declaration.SyntaxTree.GetRoot().ReplaceNode(call, call.WithAdditionalAnnotations(annotation));
            var copy = root.GetAnnotatedNodes(annotation).OfType<InvocationExpressionSyntax>().Single();

            Assert.Equal(call.Span, copy.Span);
            Assert.False(layout.TryGetSite(copy, 0, out _));
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Layout_UnannotatedNodeOfAnotherTree_FindsNoSiteAtItsOwnSpan()
    {
        var (_, declaration, _) = GetMethod("FullPairSum");
        var layout = GetLayout("FullPairSum");
        var call = Invocations(declaration).Single();

        // The same text parsed again: the call has the span of the site, but it is a node the lowering could have
        // written, with no original span to say which call it stands for
        var reparsed = CSharpSyntaxTree.ParseText(declaration.SyntaxTree.GetText());
        var copy = reparsed.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(node => node.Span == call.Span);

        Assert.False(layout.TryGetSite(copy, 0, out _));
    }

    // Dk(v) => D(k-1)(D(k-1)(v)): the size doubles with every level and passes int.MaxValue at D30
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(29, true, 1610612736)]
    [InlineData(30, false, 0)]
    [InlineData(34, false, 0)]
    public void Layout_DoublingCallChain_FailsInsteadOfWrapping(int depth, bool fits, int expectedSize)
    {
        var compilation = RoslynTestUtility.CreateCompilation(DoublingChainSource(depth));
        var kernel = compilation.GetTypeByMetadataName("WarpFrameLayoutChain.Kernels")?.GetMembers("DoubleKernel")
            .OfType<IMethodSymbol>().Single() ?? throw new InvalidOperationException("No kernel");

        Assert.Equal(fits, WarpFrameLayout.TryGet(kernel, compilation, out var layout));
        Assert.Equal(expectedSize, layout?.Size ?? 0);
    }

    // D0 has one shuffle (size 2), Dk = 1 + 2 * D(k-1), the kernel = 1 + D(depth)
    private static string DoublingChainSource(int depth)
    {
        var source = new System.Text.StringBuilder();
        source.AppendLine("using Kassyi.GSharp.Core;");
        source.AppendLine("namespace WarpFrameLayoutChain;");
        source.AppendLine("public static partial class Kernels");
        source.AppendLine("{");
        source.AppendLine("    [GpuDeviceFunction]");
        source.AppendLine("    public static int D0(int v) => Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);");
        for (var k = 1; k <= depth; k++)
        {
            source.AppendLine("    [GpuDeviceFunction]");
            source.AppendLine($"    public static int D{k}(int v) => D{k - 1}(D{k - 1}(v));");
        }

        source.AppendLine("    [GpuKernel]");
        source.AppendLine($"    public static void DoubleKernel(int[] data) {{ data[0] = D{depth}(data[0]); }}");
        source.AppendLine("}");
        return source.ToString();
    }

    private static WarpFrameLayout GetLayout(string methodName)
    {
        var (symbol, _, _) = GetMethod(methodName);
        Assert.True(WarpFrameLayout.TryGet(symbol, s_compilation, out var layout), $"{methodName} has no layout");
        return layout;
    }

    private static List<int> CallOffsets(string methodName)
    {
        var (_, declaration, _) = GetMethod(methodName);
        var layout = GetLayout(methodName);
        return Invocations(declaration)
            .Select(call => layout.TryGetCallOffset(call, 0, out var offset) ? offset : -1)
            .Where(static offset => offset >= 0)
            .ToList();
    }

    // Every static call path from the method: the frames in preorder (name, base) and the slots of their counts
    private static (List<(string Name, int Base)> Frames, List<int> Counts) WalkStaticPaths(string methodName)
    {
        var frames = new List<(string Name, int Base)>();
        var counts = new List<int>();
        Walk(GetMethod(methodName).Symbol, 0, frames, counts);
        return (frames, counts);
    }

    private static void Walk(
        IMethodSymbol method, int frameBase, List<(string Name, int Base)> frames, List<int> counts)
    {
        Assert.True(WarpFrameLayout.TryGet(method, s_compilation, out var layout));
        frames.Add((method.Name, frameBase));
        var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();
        var model = s_compilation.GetSemanticModel(declaration.SyntaxTree);
        var sites = 0;
        foreach (var call in Invocations(declaration))
        {
            if (layout.TryGetSite(call, 0, out var site))
            {
                counts.Add(frameBase + 1 + site);
                sites++;
            }
            else if (layout.TryGetCallOffset(call, 0, out var offset))
            {
                Walk(Callee(call, model).OriginalDefinition, frameBase + offset, frames, counts);
            }
        }

        Assert.Equal(layout.CollectiveCount, sites);
    }

    private static IEnumerable<InvocationExpressionSyntax> Invocations(SyntaxNode declaration) =>
        declaration.DescendantNodes().OfType<InvocationExpressionSyntax>();

    private static IMethodSymbol Callee(InvocationExpressionSyntax call, SemanticModel model) =>
        model.GetSymbolInfo(call).Symbol as IMethodSymbol
        ?? throw new InvalidOperationException("A call without a method symbol");

    private static (IMethodSymbol Symbol, MethodDeclarationSyntax Declaration, SemanticModel Model) GetMethod(
        string methodName)
    {
        var type = s_compilation.GetTypeByMetadataName(CasesType)
            ?? throw new InvalidOperationException($"Type '{CasesType}' was not found.");
        var symbol = type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
        var declaration = (MethodDeclarationSyntax)symbol.DeclaringSyntaxReferences[0].GetSyntax();
        return (symbol, declaration, s_compilation.GetSemanticModel(declaration.SyntaxTree));
    }
}
