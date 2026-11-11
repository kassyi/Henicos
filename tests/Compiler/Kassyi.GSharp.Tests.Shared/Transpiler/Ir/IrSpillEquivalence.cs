#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Compares the split plan of the IR (<see cref="IrSpillPlanner"/>) with what the syntax-based passes
/// (<see cref="ConditionalEvaluationRewriter"/>, <see cref="WarpInstructionRewriter"/>) evaluate before the split
/// points of each statement (<see cref="AstLoweringPipeline.HoistSplitPoints"/>).
/// </summary>
/// <remarks>
/// Two comparisons per statement. The plan reduced to the rules the syntax passes implement
/// (<see cref="IrSpillTrace.AsSyntaxPassesDo"/>) must equal their output exactly: a difference there is unexplained.
/// The full plan may differ from their output only by the rules they lack, and each such difference is counted under
/// the rules that make it.
/// </remarks>
internal sealed class IrSpillEquivalence
{
    /// <summary>The prefix of the reasons by which the plan differs from the syntax passes by design.</summary>
    public const string KnownPrefix = "known, ";

    private readonly ConcurrentBag<string> _unexplained = [];
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _differences = new(StringComparer.Ordinal);
    private int _statementsCompared;

    /// <summary>Statements where the reduced plan and the syntax passes differ.</summary>
    public IReadOnlyCollection<string> Unexplained => _unexplained;

    /// <summary>The differences of the full plan, and the fallbacks, by reason.</summary>
    public IReadOnlyDictionary<string, ConcurrentBag<string>> Differences => _differences;

    /// <summary>The planned statements compared, each a statement of the source that holds a split.</summary>
    public int StatementsCompared => _statementsCompared;

    /// <summary>Compares every GPU method and emitted struct member of <paramref name="trees"/>.</summary>
    public void CompareAll(Compilation compilation, SyntaxTree[] trees)
    {
        foreach (var (body, symbol, built) in IrCoverageInputs.GpuMethods(compilation, trees)
                     .Concat(IrCoverageInputs.StructMembers(compilation, trees)))
        {
            Compare((CSharpSyntaxNode)body, symbol, built, compilation);
        }
    }

    private void Compare(CSharpSyntaxNode body, IMethodSymbol symbol, IrMethod built, Compilation compilation)
    {
        var plan = IrSpillPlanner.Plan(IrClassifier.Classify(built, compilation), compilation);
        if (plan.Statements.IsEmpty)
        {
            return;
        }

        var where = IrCoverageInputs.Where(body.SyntaxTree, symbol);
        var text = body.SyntaxTree.GetText();
        var root = body.SyntaxTree.GetRoot();
        var model = compilation.GetSemanticModel(body.SyntaxTree);
        var hoisted = AstLoweringPipeline.HoistSplitPoints(body, !symbol.ReturnsVoid, model);
        var syntaxTokens = Group(IrSpillTrace.Of(hoisted.Statements, text), root);

        var planned = new Dictionary<TextSpan, (List<IrSpillTrace.Token> Full, List<IrSpillTrace.Token> Reduced,
            List<IrSpillStep> Steps)>();
        var fallbacks = new HashSet<TextSpan>();
        foreach (var statementPlan in plan.Statements)
        {
            var key = StatementOf(root, statementPlan.Statement.Source.Span).Span;
            if (statementPlan.Fallback != IrSpillFallback.None)
            {
                fallbacks.Add(key);
                Add($"fallback, {statementPlan.Fallback}", $"{where}: {text.ToString(key)}");
                continue;
            }

            if (!planned.TryGetValue(key, out var entry))
            {
                entry = ([], [], []);
                planned[key] = entry;
            }

            entry.Full.AddRange(IrSpillTrace.Of(statementPlan.Steps, text));
            entry.Reduced.AddRange(IrSpillTrace.AsSyntaxPassesDo(statementPlan.Steps, text));
            entry.Steps.AddRange(statementPlan.Steps);
        }

        foreach (var (key, (full, reduced, steps)) in planned)
        {
            if (fallbacks.Contains(key))
            {
                continue;
            }

            Interlocked.Increment(ref _statementsCompared);
            var syntax = syntaxTokens.TryGetValue(key, out var tokens) ? IrSpillTrace.Print(tokens) : "";
            var line = $"{where}: {Squeeze(text.ToString(key))}\n      ir:     {IrSpillTrace.Print(full)}\n" +
                       $"      syntax: {syntax}";
            if (IrSpillTrace.Print(reduced) != syntax)
            {
                _unexplained.Add(line + $"\n      reduced: {IrSpillTrace.Print(reduced)}");
            }
            else if (IrSpillTrace.Print(full) != syntax)
            {
                Add(KnownPrefix + Reason(steps, text), line);
            }
        }

        // A statement the syntax passes hoist from that the plan has no split for
        foreach (var (key, tokens) in syntaxTokens)
        {
            if (!planned.ContainsKey(key) && !fallbacks.Contains(key) && IsIrStatement(built, key, root))
            {
                _unexplained.Add($"{where}: {Squeeze(text.ToString(key))}\n      ir: (no plan)\n" +
                                 $"      syntax: {IrSpillTrace.Print(tokens)}");
            }
        }
    }

    // The rules of the plan that the syntax passes lack, by issue
    private static string Reason(List<IrSpillStep> steps, SourceText text)
    {
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        CollectReasons(steps, reasons);
        if (reasons.Count == 0)
        {
            reasons.Add("#991 a conditional is written as an if before the operands to its left");
        }

        return string.Join(" + ", reasons);
    }

    private static void CollectReasons(IEnumerable<IrSpillStep> steps, SortedSet<string> reasons)
    {
        foreach (var step in steps)
        {
            if (step.Kind == IrSpillStepKind.Conditional)
            {
                foreach (var branch in step.Branches)
                {
                    CollectReasons(branch, reasons);
                }

                if (step.Node is IrCoalesce)
                {
                    reasons.Add("a ?? with a split on its right is written as an if");
                }

                continue;
            }

            if (step.Kind != IrSpillStepKind.Spill || IrSpillTrace.IsMadeBySyntaxPasses(step))
            {
                continue;
            }

            reasons.Add(step.ForcedByConditional
                ? "#991 an operand before a split inside ?: / && / || is evaluated before it"
                : step.Role switch
                {
                    IrSpillRole.EarlierOperand => "#991 an earlier operand of an operation that does not split",
                    IrSpillRole.TargetPart => "#991 a part of a target location",
                    IrSpillRole.CompoundRead => "#991 the read of a compound assignment target",
                    IrSpillRole.SplitReceiver => "#991 the receiver of an extension call that splits",
                    _ => "unexpected role " + step.Role
                });
        }
    }

    // The tokens of the syntax passes by the statement of the original tree they belong to
    private static Dictionary<TextSpan, List<IrSpillTrace.Token>> Group(List<IrSpillTrace.Token> tokens,
        SyntaxNode root)
    {
        var grouped = new Dictionary<TextSpan, List<IrSpillTrace.Token>>();
        foreach (var token in tokens)
        {
            var key = StatementOf(root, token.Span).Span;
            if (!grouped.TryGetValue(key, out var list))
            {
                list = [];
                grouped[key] = list;
            }

            list.Add(token);
        }

        return grouped;
    }

    // The innermost statement of the original tree that holds span, or the expression body that does
    private static SyntaxNode StatementOf(SyntaxNode root, TextSpan span) =>
        root.FindNode(span, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .First(node => node is ArrowExpressionClauseSyntax ||
                           (node is StatementSyntax && (node is not BlockSyntax || node.Span == span)));

    // Whether the statement at key is a statement of the IR (not inside an opaque statement)
    private static bool IsIrStatement(IrMethod method, TextSpan key, SyntaxNode root)
    {
        var found = false;
        IrNodeChildren.Visit(method.Body, node =>
        {
            if (node is IrStatement statement and not (IrBlock or IrOpaqueStatement) &&
                StatementOf(root, statement.Source.Span).Span == key)
            {
                found = true;
            }
        });
        return found;
    }

    private void Add(string reason, string line) =>
        _differences.GetOrAdd(reason, static _ => []).Add(line);

    private static string Squeeze(string source) => IrSpillTrace.Squeeze(source);
}
