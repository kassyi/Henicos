#nullable enable
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Runs the first round of the lowering with the split pass of the IR in front of the syntax passes
/// (<see cref="AstLoweringPipeline.HoistSplitPointsWithIr"/>) on every GPU method of the corpus and compares it with
/// the first round of the syntax passes alone (<see cref="AstLoweringPipeline.HoistSplitPoints"/>).
/// </summary>
/// <remarks>
/// Three properties per method:
/// <list type="number">
/// <item><description>Each statement the IR writes out evaluates, before its splits, what its plans say, in the same
/// order (<see cref="IrSpillTrace"/>, checks included).</description></item>
/// <item><description>Every other statement is what the syntax passes make of it, up to the numbers of the
/// temporaries (<see cref="IrSpillTrace.RenumberTemporaries"/>).</description></item>
/// <item><description>The syntax passes change nothing the IR wrote: run on the output of the split pass of the IR with
/// its marks removed, they give the same tree as with the marks, which they pass through.</description></item>
/// </list>
/// The statements left to the syntax passes are counted by reason (reported, not asserted).
/// </remarks>
internal sealed class IrSpillApplication
{
    private readonly ConcurrentBag<string> _failures = [];
    private readonly ConcurrentDictionary<string, int> _reasons = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<string> _mixedMethods = [];
    private int _lowered;
    private int _comparedUnchanged;

    /// <summary>The statements or methods where a property does not hold.</summary>
    public IReadOnlyCollection<string> Failures => _failures;

    /// <summary>The statements left to the syntax passes, by reason.</summary>
    public IReadOnlyDictionary<string, int> Reasons => _reasons;

    /// <summary>
    /// The methods with statements written by the IR and statements with splits left to the syntax passes.
    /// </summary>
    public IReadOnlyCollection<string> MixedMethods => _mixedMethods;

    /// <summary>The statements of the source the split pass of the IR wrote out.</summary>
    public int Lowered => _lowered;

    /// <summary>The statements left to the syntax passes that were compared with the syntax passes alone.</summary>
    public int ComparedUnchanged => _comparedUnchanged;

    /// <summary>Checks every GPU method and emitted struct member of <paramref name="trees"/>.</summary>
    public void CheckAll(Compilation compilation, SyntaxTree[] trees)
    {
        foreach (var (body, symbol, _) in IrCoverageInputs.GpuMethods(compilation, trees)
                     .Concat(IrCoverageInputs.StructMembers(compilation, trees)))
        {
            Check((CSharpSyntaxNode)body, symbol, compilation);
        }
    }

    private void Check(CSharpSyntaxNode body, IMethodSymbol symbol, Compilation compilation)
    {
        var where = IrCoverageInputs.Where(body.SyntaxTree, symbol);
        var model = compilation.GetSemanticModel(body.SyntaxTree);
        var withIr = AstLoweringPipeline.HoistSplitPointsWithIr(body, !symbol.ReturnsVoid, model, symbol,
            out var outcomes);
        if (outcomes.Count == 0)
        {
            return;
        }

        var text = body.SyntaxTree.GetText();
        var root = body.SyntaxTree.GetRoot();
        var lowered = new List<TextSpan>();
        foreach (var outcome in outcomes)
        {
            if (outcome.IsLowered)
            {
                Interlocked.Increment(ref _lowered);
                lowered.Add(outcome.Source);
                CheckTrace(outcome, where, text);
            }
            else
            {
                var reason = outcome.Fallback != IrSpillFallback.None
                    ? "fallback " + outcome.Fallback
                    : "skip " + outcome.Skip;
                _reasons.AddOrUpdate(reason, 1, static (_, count) => count + 1);
            }
        }

        if (lowered.Count > 0 && lowered.Count < outcomes.Count)
        {
            _mixedMethods.Add(where);
        }

        var withoutIr = AstLoweringPipeline.HoistSplitPoints(body, !symbol.ReturnsVoid, model);
        CompareLeftStatements(withIr, withoutIr, lowered, root, where, text);
        CheckPassedThrough(body, symbol, model, withIr, where);
    }

    // 1. What the written statements evaluate before their splits is what their plans say
    private void CheckTrace(IrSplitLoweringOutcome outcome, string where, SourceText text)
    {
        var planned = IrSpillTrace.Print(outcome.Plans.SelectMany(plan =>
            IrSpillTrace.Of(WithoutStoredCompounds(plan.Steps, []), text)));
        var written = IrSpillTrace.Print(IrSpillTrace.Of(OwnStatements(outcome.Printed), text));
        if (planned != written)
        {
            _failures.Add($"{where}: {IrSpillTrace.Squeeze(text.ToString(outcome.Source))}\n" +
                          $"    plan:    {planned}\n    written: {written}");
        }
    }

    // [WHY] A compound assignment evaluated before a later split is its temporary once its store step completed it:
    // the spill of the compound writes nothing of its own
    private static ImmutableArray<IrSpillStep> WithoutStoredCompounds(ImmutableArray<IrSpillStep> steps,
        HashSet<IrExpression> stored)
    {
        var kept = ImmutableArray.CreateBuilder<IrSpillStep>(steps.Length);
        foreach (var step in steps)
        {
            switch (step)
            {
                case IrSpillCompoundStore store:
                    stored.Add(store.Compound);
                    kept.Add(step);
                    break;
                case IrSpillValue value when stored.Contains(value.Node):
                    break;
                case IrSpillConditional conditional:
                    kept.Add(conditional with
                    {
                        Branches = conditional.Branches
                            .Select(branch => WithoutStoredCompounds(branch, stored))
                            .ToImmutableArray()
                    });
                    break;
                default:
                    kept.Add(step);
                    break;
            }
        }

        return kept.ToImmutable();
    }

    // An if whose condition was written keeps its branches, which belong to statements of their own
    private static IEnumerable<StatementSyntax> OwnStatements(ImmutableArray<StatementSyntax> printed) =>
        printed.Select(static statement => statement is IfStatementSyntax @if &&
                                           @if.HasAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind)
            ? @if.WithStatement(SyntaxFactory.Block()).WithElse(null)
            : statement);

    // 2. A statement left to the syntax passes is what they make of it alone (a statement that holds a written one is
    // left out)
    private void CompareLeftStatements(BlockSyntax withIr, BlockSyntax withoutIr, List<TextSpan> lowered,
        SyntaxNode root, string where, SourceText text)
    {
        var groupsWithIr = Groups(withIr, root);
        foreach (var (source, statements) in Groups(withoutIr, root))
        {
            if (lowered.Any(span => source.Contains(span)))
            {
                continue;
            }

            Interlocked.Increment(ref _comparedUnchanged);
            if (!groupsWithIr.TryGetValue(source, out var other) ||
                !SyntaxFactory.AreEquivalent(
                    IrSpillTrace.RenumberTemporaries(SyntaxFactory.Block(statements)),
                    IrSpillTrace.RenumberTemporaries(SyntaxFactory.Block(other)),
                    topLevel: false))
            {
                _failures.Add($"{where}: {IrSpillTrace.Squeeze(text.ToString(source))}\n" +
                              "    left to the syntax passes, but not what they make of it alone");
            }
        }
    }

    // The statements of each list, by the statement of the source they belong to: a run of statements ends with the
    // one that carries an original span (the temporaries before it have none); the statements the IR wrote are
    // left out
    private static Dictionary<TextSpan, List<StatementSyntax>> Groups(BlockSyntax body, SyntaxNode root)
    {
        var groups = new Dictionary<TextSpan, List<StatementSyntax>>();
        var lists = body.DescendantNodesAndSelf()
            .Select(static node => node switch
            {
                BlockSyntax block => block.Statements,
                SwitchSectionSyntax section => section.Statements,
                _ => default(SyntaxList<StatementSyntax>?)
            })
            .OfType<SyntaxList<StatementSyntax>>();
        foreach (var list in lists)
        {
            var run = new List<StatementSyntax>();
            foreach (var statement in list)
            {
                if (statement.HasAnnotations(LoweringAnnotations.IrLoweredAnnotationKind))
                {
                    continue;
                }

                run.Add(statement);
                if (OriginalSpan(statement) is not { } span)
                {
                    continue;
                }

                var source = IrSplitStatements.StatementOf(root, span).Span;
                if (!groups.TryGetValue(source, out var group))
                {
                    group = [];
                    groups[source] = group;
                }

                group.AddRange(run);
                run.Clear();
            }
        }

        return groups;
    }

    private static TextSpan? OriginalSpan(SyntaxNode node)
    {
        foreach (var annotation in node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
        {
            if (LoweringAnnotations.TryParseOriginalSpanData(annotation.Data, out var start, out var length, out _,
                    out _))
            {
                return new TextSpan(start, length);
            }
        }

        return null;
    }

    // 3. The syntax passes, run on what the IR wrote without its marks, change nothing of it
    private void CheckPassedThrough(CSharpSyntaxNode body, IMethodSymbol symbol, SemanticModel model,
        BlockSyntax withIr, string where)
    {
        var names = new LoweringTempNames();
        var prepared = AstLoweringPipeline.Prepare(body, !symbol.ReturnsVoid, model);
        var bodyModel = body.SyntaxTree == model.SyntaxTree
            ? model
            : model.Compilation.GetSemanticModel(body.SyntaxTree);
        var irLowered = new IrSplitLoweringPass(bodyModel, symbol, body, names).Rewrite(prepared);
        var unmarked = new MarkRemover().Visit(irLowered) as BlockSyntax ??
                       throw new InvalidOperationException("The marks were removed with the block.");
        var conditionalsLowered = new ConditionalEvaluationRewriter(names).Rewrite(unmarked);
        var hoisted = new WarpInstructionRewriter(bodyModel, names).Rewrite(conditionalsLowered);
        if (!SyntaxFactory.AreEquivalent(hoisted, withIr, topLevel: false))
        {
            _failures.Add($"{where}: the syntax passes change a statement the IR wrote");
        }
    }

    // Removes the marks of the statements the IR wrote, so that the syntax passes visit them
    private sealed class MarkRemover : CSharpSyntaxRewriter
    {
        public override SyntaxNode? Visit(SyntaxNode? node) =>
            base.Visit(node)?.WithoutAnnotations(LoweringAnnotations.IrLoweredAnnotationKind);
    }
}
