#nullable enable
using System.Diagnostics;
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Frames;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Runs <see cref="IrLivenessAnalysis.Analyze"/> on every GPU method of the corpus and compares
/// <see cref="IrLiveness.IsLiveAcrossSourceSplit"/> for non-const locals against
/// <see cref="CrossBarrierLiveness.FindLiveAcrossSplit"/>.
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrLivenessEquivalenceTests(ITestOutputHelper output)
{
    private const string ExpectedFile = "IrLivenessDifferences.expected.yml";

    private static readonly string[] s_header =
    [
        "The status and variable counts of every GPU method of the IR corpus (TestCases/ and every *.cu.cs kernel",
        "file) when comparing IrLivenessAnalysis (IsLiveAcrossSourceSplit) with CrossBarrierLiveness.",
        "IrLivenessEquivalenceTests.EveryGpuMethod_MatchesTheCrossBarrierLiveness builds this list on every run",
        "and fails on any difference; a received file goes to the test received folder.",
        "",
        "  file:   the source file, relative to tests/Compiler/Kassyi.GSharp.Tests.Shared",
        "  method: the containing type and the method",
        "  status: Compared, NoStateMachine, Reconvergence, IrNotBuilt, Fallback:<kind>, OpaqueStatement,",
        "          Skip:<kind>, OpaqueExpression, NoSplit, or NoAnalyzerGraph",
        "  kind:   ComparedVariable, ExcludedConst, or OutDeclarationReadByAnalyzer",
        "  count:  1 per method for status, or the variable count for kind"
    ];

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_MatchesTheCrossBarrierLiveness()
    {
        var stopwatch = Stopwatch.StartNew();
        var counts = new IrCorpusCounts();
        var violations = new ConcurrentBag<string>();
        var unexplained = new ConcurrentBag<(bool IrLive, string Text)>();
        var compared = 0;

        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var tree in trees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(declaration) is not { } symbol ||
                        !GpuExecutionSpace.IsGpuFunction(symbol))
                    {
                        continue;
                    }

                    var where = IrCoverageInputs.Locate(tree, symbol);
                    var status = CompareMethod(declaration, symbol, model, compilation, where, counts, violations,
                        unexplained);
                    if (status == "Compared")
                    {
                        Interlocked.Increment(ref compared);
                    }

                    counts.Add(where, ("status", status));
                }
            }
        }, everyKernelFile: true);

        output.WriteLine($"{compared} methods compared, {unexplained.Count} unexplained, " +
                         $"{violations.Count} invariant violations, {stopwatch.ElapsedMilliseconds} ms");
        foreach (var total in counts.Totals())
        {
            output.WriteLine(total);
        }

        var rendered = counts.Render(s_header);
        var receivedPath = IrExpectedFiles.ReceivedPath("IrLivenessDifferences", ".yml");
        File.WriteAllText(receivedPath, rendered);
        output.WriteLine("Received: " + receivedPath);

        Assert.True(compared > 0, "Expected at least one compared GPU method.");
        Assert.True(violations.IsEmpty,
            string.Join("\n", violations.OrderBy(static v => v, StringComparer.Ordinal).Take(50)));
        Assert.True(unexplained.IsEmpty,
            string.Join("\n", unexplained
                .OrderByDescending(static u => u.IrLive)
                .ThenBy(static u => u.Text, StringComparer.Ordinal)
                .Select(static u => u.Text)
                .Take(50)));
        IrExpectedFiles.AssertMatches(ExpectedFile, rendered, output);
    }

    private static string CompareMethod(MethodDeclarationSyntax declaration, IMethodSymbol symbol, SemanticModel model,
        Compilation compilation, IrCorpusMethod where, IrCorpusCounts counts, ConcurrentBag<string> violations,
        ConcurrentBag<(bool IrLive, string Text)> unexplained)
    {
        if (!declaration.HasBody())
        {
            return "NoStateMachine";
        }

        var isKernel = symbol.HasAttribute(KnownAttributes.GpuKernel);
        if (!isKernel &&
            (IteratorTypeResolver.IsDeviceIteratorMethod(symbol) &&
             !IteratorTypeResolver.IsRecursiveIterator(symbol, compilation) ||
             !SuspensionAnalysis.RequiresStateMachine(symbol, compilation)))
        {
            return "NoStateMachine";
        }

        var legacy = AstLoweringPipeline.Lower(declaration, model, symbol, isDeviceFunction: !isKernel);
        if (HasReconvergence(legacy.LoweredBody))
        {
            return "Reconvergence";
        }

        if (IrBuilder.TryBuild(symbol, compilation, CancellationToken.None) is not { } built)
        {
            return "IrNotBuilt";
        }

        var classified = IrClassifier.Classify(built, compilation);
        if (IrSplitLowering.Apply(classified, compilation, out var failure) is not { } splitLowered)
        {
            return failure.Kind switch
            {
                IrSplitLoweringFailureKind.Fallback => "Fallback:" + failure.Fallback,
                IrSplitLoweringFailureKind.OpaqueStatement => "OpaqueStatement",
                IrSplitLoweringFailureKind.Skip => "Skip:" + failure.Skip,
                _ => throw new InvalidOperationException($"Unexpected failure kind: {failure.Kind}")
            };
        }

        var linear = IrLinearizer.Linearize(splitLowered, compilation);
        var machine = IrStateMachineBuilder.Build(linear, isDeviceFunction: !isKernel, built.Body.Source, compilation);
        if (IrLivenessAnalysis.HoldsOpaque(machine))
        {
            return "OpaqueExpression";
        }

        var liveness = IrLivenessAnalysis.Analyze(machine);
        foreach (var violation in IrLivenessInvariants.Verify(machine, liveness))
        {
            violations.Add($"{where.File} {where.Method}: {violation}");
        }

        if (!liveness.HasSplit)
        {
            return "NoSplit";
        }

        if (!new MethodFlowGraph(model, declaration).TryGetLiveness(out var flowGraph, out var recorder))
        {
            return "NoAnalyzerGraph";
        }

        CompareLocals(where, liveness, flowGraph, recorder, counts, unexplained);
        return "Compared";
    }

    private static void CompareLocals(IrCorpusMethod where, IrLiveness liveness, SplicedFlowGraph flowGraph,
        LivenessRecorder recorder, IrCorpusCounts counts, ConcurrentBag<(bool IrLive, string Text)> unexplained)
    {
        var trackedVars = new List<IrLivenessVariable>();
        var trackedSymbols = new List<ISymbol>();
        foreach (var variable in liveness.Variables)
        {
            if (variable.Variable.Kind != IrVariableKind.Local)
            {
                continue;
            }

            if (variable.Variable.Symbol is ILocalSymbol { IsConst: true })
            {
                counts.Add(where, ("kind", "ExcludedConst"));
                continue;
            }

            if (variable.Variable.Symbol is { } localSymbol)
            {
                trackedVars.Add(variable);
                trackedSymbols.Add(localSymbol);
            }
        }

        var analyzerLive = CrossBarrierLiveness.FindLiveAcrossSplit(flowGraph, recorder, trackedSymbols);
        for (var i = 0; i < trackedVars.Count; i++)
        {
            var variable = trackedVars[i];
            var irResult = liveness.IsLiveAcrossSourceSplit(variable);
            var analyzerResult = analyzerLive[i];
            counts.Add(where, ("kind", "ComparedVariable"));
            if (irResult == analyzerResult)
            {
                continue;
            }

            if (!irResult && analyzerResult && variable.Form == IrDeclarationForm.Reference)
            {
                counts.Add(where, ("kind", "OutDeclarationReadByAnalyzer"));
                continue;
            }

            unexplained.Add((irResult,
                $"{where.File} {where.Method} ({variable.Variable.Name}): IR {irResult}, Analyzer {analyzerResult}"));
        }
    }

    private static bool HasReconvergence(BlockSyntax loweredBody) =>
        loweredBody.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(static invocation =>
                invocation.Expression is IdentifierNameSyntax id &&
                id.Identifier.ValueText == GSharpInternalNames.Prelude.WarpReconvergeArrive);
}
