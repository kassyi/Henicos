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
/// The full plan may differ from their output only by the rules they lack; each such statement is recorded with its
/// reason and both traces (<see cref="Known"/>), which the test compares line by line with an expected file. A
/// difference of the position of a conditional alone is recognized only when everything else matches.
/// Every planned statement is also rewritten (<see cref="IrSpillRewriter"/>): the statements it skips are counted by
/// reason, and the invariants of the others (<see cref="IrLoweredInvariants"/>) must hold.
/// </remarks>
internal sealed class IrSpillEquivalence
{
    private readonly ConcurrentBag<string> _unexplained = [];
    private readonly ConcurrentBag<string> _known = [];
    private readonly ConcurrentDictionary<string, int> _fallbacks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _skips = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<string> _violations = [];
    private readonly ConcurrentBag<string> _mixedMethods = [];
    private int _statementsCompared;
    private int _statementsWithACheck;
    private int _statementsRewritten;

    /// <summary>Statements where the reduced plan and the syntax passes differ.</summary>
    public IReadOnlyCollection<string> Unexplained => _unexplained;

    /// <summary>The statements where the full plan differs by a rule the syntax passes lack: reason and
    /// traces.</summary>
    public IReadOnlyCollection<string> Known => _known;

    /// <summary>The statements left to the syntax passes, by reason (reported, not asserted).</summary>
    public IReadOnlyDictionary<string, int> Fallbacks => _fallbacks;

    /// <summary>The planned statements compared, each a statement of the source that holds a split.</summary>
    public int StatementsCompared => _statementsCompared;

    /// <summary>The planned statements the rewrite skips, by reason (reported, not asserted).</summary>
    public IReadOnlyDictionary<string, int> Skips => _skips;

    /// <summary>The violations of the invariants of the rewritten statements.</summary>
    public IReadOnlyCollection<string> Violations => _violations;

    /// <summary>
    /// The methods where some statements that hold a split are rewritten on the IR and others stay on the syntax
    /// passes (a fallback or a skip): their temporaries are numbered in a different order once both run.
    /// </summary>
    public IReadOnlyCollection<string> MixedMethods => _mixedMethods;

    /// <summary>The planned statements of the IR whose plan checks a location before a split.</summary>
    public int StatementsWithACheck => _statementsWithACheck;

    /// <summary>The planned statements of the IR that the rewrite writes out.</summary>
    public int StatementsRewritten => _statementsRewritten;

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

        var method = new MethodContext(IrCoverageInputs.Where(body.SyntaxTree, symbol), body.SyntaxTree.GetText(),
            body.SyntaxTree.GetRoot());
        var hoisted = AstLoweringPipeline.HoistSplitPoints(body, !symbol.ReturnsVoid,
            compilation.GetSemanticModel(body.SyntaxTree));
        var syntax = Group(IrSpillTrace.Of(hoisted.Statements, method.Text), method.Root);
        var (planned, fallbacks) = PlanByStatement(plan, method);
        foreach (var (key, statement) in planned)
        {
            if (!fallbacks.Contains(key))
            {
                CompareStatement(method, key, statement, syntax.TryGetValue(key, out var tokens) ? tokens : []);
            }
        }

        ReportUnplanned(built, method, syntax, planned, fallbacks);
        Rewrite(plan, method.Where, compilation);
    }

    // Rewrites every planned statement; a method is mixed when it has rewritten statements and statements left to the
    // syntax passes
    private void Rewrite(IrSpillPlan plan, string where, Compilation compilation)
    {
        var (rewritten, left) = (0, 0);
        foreach (var statement in plan.Statements)
        {
            if (statement.Fallback != IrSpillFallback.None)
            {
                left++;
                continue;
            }

            if (HasCheck(statement.Steps))
            {
                Interlocked.Increment(ref _statementsWithACheck);
            }

            var application = IrSpillRewriter.Apply(statement);
            if (!application.TryGetLowered(out var lowered))
            {
                left++;
                _skips.AddOrUpdate(application.Skip.ToString(), 1, static (_, count) => count + 1);
                continue;
            }

            rewritten++;
            Interlocked.Increment(ref _statementsRewritten);
            foreach (var violation in IrLoweredInvariants.Verify(statement, lowered, compilation))
            {
                _violations.Add($"{where}: {violation}");
            }
        }

        if (rewritten > 0 && left > 0)
        {
            _mixedMethods.Add(where);
        }
    }

    private static bool HasCheck(ImmutableArray<IrSpillStep> steps) =>
        steps.Any(static step => step is IrSpillCheck ||
                                 (step is IrSpillConditional conditional && conditional.Branches.Any(HasCheck)));

    // The plans by the statement of the source they belong to (a declaration of several locals has one per local)
    private (Dictionary<TextSpan, StatementPlan> Planned, HashSet<TextSpan> Fallbacks) PlanByStatement(
        IrSpillPlan plan, MethodContext method)
    {
        var planned = new Dictionary<TextSpan, StatementPlan>();
        var fallbacks = new HashSet<TextSpan>();
        foreach (var statementPlan in plan.Statements)
        {
            var key = StatementOf(method.Root, statementPlan.Statement.Source.Span).Span;
            if (statementPlan.Fallback != IrSpillFallback.None)
            {
                fallbacks.Add(key);
                _fallbacks.AddOrUpdate(statementPlan.Fallback.ToString(), 1, static (_, count) => count + 1);
                continue;
            }

            if (!planned.TryGetValue(key, out var entry))
            {
                entry = new StatementPlan();
                planned[key] = entry;
            }

            entry.Full.AddRange(IrSpillTrace.Of(statementPlan.Steps, method.Text));
            entry.Reduced.AddRange(IrSpillTrace.AsSyntaxPassesDo(statementPlan.Steps, method.Text));
            entry.Steps.AddRange(statementPlan.Steps);
        }

        return (planned, fallbacks);
    }

    private void CompareStatement(MethodContext method, TextSpan key, StatementPlan statement,
        List<IrSpillTrace.Token> syntaxTokens)
    {
        Interlocked.Increment(ref _statementsCompared);
        var syntax = IrSpillTrace.Print(syntaxTokens);
        var full = IrSpillTrace.Print(statement.Full);
        var line = $"{method.Where}: {IrSpillTrace.Squeeze(method.Text.ToString(key))}\n" +
                   $"    ir:     {full}\n    syntax: {syntax}";
        if (IrSpillTrace.Print(statement.Reduced) != syntax)
        {
            _unexplained.Add(line + $"\n    reduced: {IrSpillTrace.Print(statement.Reduced)}");
        }
        else if (full != syntax)
        {
            if (Reason(statement.Steps, statement.Full, syntaxTokens) is { } reason)
            {
                _known.Add(reason + "\n  " + line);
            }
            else
            {
                _unexplained.Add(line + "\n    (no rule of the plan explains the difference)");
            }
        }
    }

    // A statement the syntax passes hoist from that the plan has no split for
    private void ReportUnplanned(IrMethod built, MethodContext method,
        Dictionary<TextSpan, List<IrSpillTrace.Token>> syntax, Dictionary<TextSpan, StatementPlan> planned,
        HashSet<TextSpan> fallbacks)
    {
        foreach (var (key, tokens) in syntax)
        {
            if (!planned.ContainsKey(key) && !fallbacks.Contains(key) && IsIrStatement(built, key, method.Root))
            {
                _unexplained.Add($"{method.Where}: {IrSpillTrace.Squeeze(method.Text.ToString(key))}\n" +
                                 $"    ir: (no plan)\n    syntax: {IrSpillTrace.Print(tokens)}");
            }
        }
    }

    // The rules of the plan that the syntax passes lack; without one, only the position of a conditional may differ
    private static string? Reason(List<IrSpillStep> steps, List<IrSpillTrace.Token> full,
        List<IrSpillTrace.Token> syntax)
    {
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        CollectReasons(steps, reasons);
        if (reasons.Count > 0)
        {
            return string.Join(" + ", reasons);
        }

        return OnlyConditionalsMoved(full, syntax)
            ? "#991 a conditional is written as an if before the operands to its left"
            : null;
    }

    // The same tokens apart from the conditionals, and the same conditionals in the same order
    private static bool OnlyConditionalsMoved(List<IrSpillTrace.Token> full, List<IrSpillTrace.Token> syntax) =>
        Texts(full, conditionals: false).SequenceEqual(Texts(syntax, conditionals: false)) &&
        Texts(full, conditionals: true).SequenceEqual(Texts(syntax, conditionals: true));

    private static IEnumerable<string> Texts(List<IrSpillTrace.Token> tokens, bool conditionals) =>
        tokens
            .Where(token => token.Text.StartsWith("cond{", StringComparison.Ordinal) == conditionals)
            .Select(static token => token.Text);

    private static void CollectReasons(IEnumerable<IrSpillStep> steps, SortedSet<string> reasons)
    {
        foreach (var step in steps)
        {
            switch (step)
            {
                case IrSpillConditional conditional:
                    foreach (var branch in conditional.Branches)
                    {
                        CollectReasons(branch, reasons);
                    }

                    if (conditional.Node is IrCoalesce)
                    {
                        reasons.Add("#927 a ?? with a split on its right is written as an if");
                    }

                    break;
                case IrSpillValue spill when !IrSpillTrace.IsMadeBySyntaxPasses(spill):
                    reasons.Add(ReasonOf(spill));
                    break;
                case IrSpillCheck:
                    reasons.Add("#991 a location C# checks before the split");
                    break;
            }
        }
    }

    private static string ReasonOf(IrSpillValue spill) =>
        spill.ForcedByConditional
            ? "#991 an operand before a split inside ?: / && / || is evaluated before it"
            : spill.Role switch
            {
                IrSpillRole.EarlierOperand => "#991 an earlier operand of an operation that does not split",
                IrSpillRole.TargetPart => "#991 a part of a target location that the split may change",
                IrSpillRole.CompoundRead => "#991 the read of a compound assignment target",
                IrSpillRole.SplitReceiver => "#927 the receiver of an extension call that splits",
                _ => "unexpected role " + spill.Role
            };

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
    internal static bool IsIrStatement(IrMethod method, TextSpan key, SyntaxNode root) =>
        IrNodeChildren.Exists(method.Body, node =>
            node is IrStatement statement and not (IrBlock or IrOpaqueStatement) &&
            StatementOf(root, statement.Source.Span).Span == key);

    private sealed record MethodContext(string Where, SourceText Text, SyntaxNode Root);

    // The plans of the IR statements of one source statement
    private sealed class StatementPlan
    {
        public List<IrSpillTrace.Token> Full { get; } = [];

        public List<IrSpillTrace.Token> Reduced { get; } = [];

        public List<IrSpillStep> Steps { get; } = [];
    }
}
