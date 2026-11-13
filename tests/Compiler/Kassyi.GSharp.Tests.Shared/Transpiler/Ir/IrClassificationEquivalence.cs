#nullable enable
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Compares the classify pass with the decisions the syntax-based passes make on the same method: the split point
/// annotation of each call (<see cref="AstLoweringPipeline.ClassifySplitPoint"/>) and the effects of each expression
/// (<see cref="ExpressionEffects"/>).
/// </summary>
/// <remarks>
/// A difference of effects is compared at the innermost node that differs; an outer node that differs only because
/// an operand differs is counted as such, and one that differs beyond what its operands explain is reported on its
/// own. Each known difference is checked by the exact delta it names, not by the shape of the node alone.
/// </remarks>
internal sealed class IrClassificationEquivalence
{
    /// <summary>The prefix of the reasons the IR and the syntax walk differ by design.</summary>
    public const string KnownPrefix = "known, ";

    private readonly ConcurrentBag<string> _splitMismatches = [];
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _effectMismatches =
        new(StringComparer.Ordinal);
    private int _callsCompared;
    private int _splitCallsCompared;
    private int _invocationsInOpaque;
    private int _splitInvocationsInOpaque;
    private int _removedSplitInvocationsInOpaque;
    private int _expressionsCompared;
    private int _expressionsWithOpaque;
    private int _outerExplainedByOperands;
    private int _structMembers;

    public IReadOnlyCollection<string> SplitMismatches => _splitMismatches;

    /// <summary>
    /// The effect differences by reason: a known one (<see cref="KnownPrefix"/>) or an unexplained one.
    /// </summary>
    public IReadOnlyDictionary<string, ConcurrentBag<string>> EffectMismatches => _effectMismatches;

    public int CallsCompared => _callsCompared;

    public int SplitCallsCompared => _splitCallsCompared;

    public int InvocationsInOpaque => _invocationsInOpaque;

    public int SplitInvocationsInOpaque => _splitInvocationsInOpaque;

    /// <summary>Split invocations in opaque nodes that never run: C# removes them or the call they are in.</summary>
    public int RemovedSplitInvocationsInOpaque => _removedSplitInvocationsInOpaque;

    public int ExpressionsCompared => _expressionsCompared;

    public int ExpressionsWithOpaque => _expressionsWithOpaque;

    /// <summary>Outer expressions whose difference is the difference of an operand.</summary>
    public int OuterExplainedByOperands => _outerExplainedByOperands;

    public int StructMembers => _structMembers;

    /// <summary>Compares every GPU method and emitted struct member of <paramref name="trees"/>.</summary>
    public void CompareAll(Compilation compilation, SyntaxTree[] trees)
    {
        foreach (var (body, symbol, built) in IrCoverageInputs.GpuMethods(compilation, trees))
        {
            Compare(body, symbol, built, compilation);
        }

        foreach (var (body, symbol, built) in IrCoverageInputs.StructMembers(compilation, trees))
        {
            Interlocked.Increment(ref _structMembers);
            Compare(body, symbol, built, compilation);
        }
    }

    private void Compare(SyntaxNode body, IMethodSymbol symbol, IrMethod built, Compilation compilation)
    {
        var where = IrCoverageInputs.Where(body.SyntaxTree, symbol);
        var method = IrClassifier.Classify(built, compilation);
        foreach (var violation in IrInvariants.VerifyClassified(method))
        {
            _splitMismatches.Add($"{where}: {violation}");
        }

        CompareSplits(method, body, compilation, where);
        CompareEffects(method, body.SyntaxTree, compilation, where);
    }

    // Every invocation outside the opaque nodes is a call of the IR with the split the lowering annotates, every call
    // of the IR is an invocation, every split invocation that runs inside an opaque node is under a node marked as
    // holding one, and every node marked so holds one
    private void CompareSplits(IrMethod method, SyntaxNode body, Compilation compilation, string where)
    {
        var calls = new Dictionary<TextSpan, IrCall>();
        var opaque = new List<(TextSpan Span, IrOpaqueSplit Split)>();
        foreach (var node in IrNodeChildren.DescendantsAndSelf(method.Body))
        {
            switch (node)
            {
                case IrCall call:
                    calls[call.Source.Span] = call;
                    break;
                case IrOpaqueExpression expression:
                    opaque.Add((expression.Syntax.Span, expression.Split));
                    break;
                case IrOpaqueStatement statement:
                    opaque.Add((statement.Syntax.Span, statement.Split));
                    break;
            }
        }

        var model = compilation.GetSemanticModel(body.SyntaxTree);
        var holdsRunningSplit = new bool[opaque.Count];
        var matched = 0;
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol callee)
            {
                continue;
            }

            var expected = Describe(AstLoweringPipeline.ClassifySplitPoint(callee, compilation));
            if (calls.TryGetValue(invocation.Span, out var call))
            {
                matched++;
                CompareCall(call, invocation, expected, where);
            }
            else if (opaque.FindIndex(o => o.Span.Contains(invocation.Span)) is var index and >= 0)
            {
                if (CompareInOpaque(opaque[index].Split, invocation, expected, model, where))
                {
                    holdsRunningSplit[index] = true;
                }
            }
            else
            {
                _splitMismatches.Add($"{where}: {invocation}: an invocation outside the opaque nodes has no call");
            }
        }

        if (matched != calls.Count)
        {
            _splitMismatches.Add($"{where}: {calls.Count - matched} calls of the IR are no invocation");
        }

        CompareHeldSplits(opaque, holdsRunningSplit, where);
    }

    // Every opaque node marked as holding a split holds a split invocation that runs
    private void CompareHeldSplits(List<(TextSpan Span, IrOpaqueSplit Split)> opaque, bool[] holdsRunningSplit,
        string where)
    {
        for (var i = 0; i < opaque.Count; i++)
        {
            if (opaque[i].Split == IrOpaqueSplit.HoldsSplit && !holdsRunningSplit[i])
            {
                _splitMismatches.Add(
                    $"{where}: an opaque node at {opaque[i].Span} is marked {IrOpaqueSplit.HoldsSplit} but no split " +
                    "point that runs is in it");
            }
        }
    }

    private void CompareCall(IrCall call, InvocationExpressionSyntax invocation, string expected, string where)
    {
        Interlocked.Increment(ref _callsCompared);
        var actual = Describe(LoweringAnnotations.CreateSplitAnnotation(call.Classification.Split,
            call.Method.ReturnsVoid));
        if (actual != "none")
        {
            Interlocked.Increment(ref _splitCallsCompared);
        }

        if (actual != expected)
        {
            _splitMismatches.Add($"{where}: {invocation}: lowering {expected}, IR {actual}");
        }
    }

    // Whether the invocation is a split point that runs; one that C# removes, or that is inside a removed call, never
    // runs and is counted apart
    private bool CompareInOpaque(IrOpaqueSplit split, InvocationExpressionSyntax invocation, string expected,
        SemanticModel model, string where)
    {
        Interlocked.Increment(ref _invocationsInOpaque);
        if (expected == "none")
        {
            return false;
        }

        if (RemovedCalls.IsInRemovedCall(invocation, model))
        {
            Interlocked.Increment(ref _removedSplitInvocationsInOpaque);
            return false;
        }

        Interlocked.Increment(ref _splitInvocationsInOpaque);
        if (split != IrOpaqueSplit.HoldsSplit)
        {
            _splitMismatches.Add($"{where}: {invocation}: a split point under an opaque node marked {split}");
        }

        return true;
    }

    // The effects of the outermost node of each span that has the span of an expression of the source
    private void CompareEffects(IrMethod method, SyntaxTree tree, Compilation compilation, string where)
    {
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var seen = new HashSet<TextSpan>();
        var found = new List<EffectDifference>();
        foreach (var node in IrNodeChildren.DescendantsAndSelf(method.Body))
        {
            if (node is not IrExpression expression ||
                (expression.Source.Synthesized && expression is not IrConversion) ||
                !seen.Add(expression.Source.Span) ||
                FindExpression(root, expression.Source.Span) is not { } syntax)
            {
                continue;
            }

            if (IrInvariants.ContainsOpaque(expression))
            {
                Interlocked.Increment(ref _expressionsWithOpaque);
                continue;
            }

            Interlocked.Increment(ref _expressionsCompared);
            var isLocation = expression.Category is IrCategory.Location or IrCategory.ReadOnlyLocation;
            var old = isLocation ? ExpressionEffects.OfLocation(syntax, model) : ExpressionEffects.Of(syntax, model);
            var ir = isLocation ? IrEffects.OfLocation(expression, compilation) : IrEffects.Of(expression, compilation);
            if (Kind(old, ir) is { } kind)
            {
                found.Add(new EffectDifference(expression, kind, old, ir,
                    $"{where}: {syntax} ({(isLocation ? "location" : "value")}): " +
                    $"syntax reads {old.Reads} writes {old.Writes}, IR reads {ir.Reads} writes {ir.Writes}"));
            }
        }

        Report(found, compilation);
    }

    // The innermost differences by their reason; an outer difference by its own reason unless the differences of its
    // operands explain it
    private void Report(List<EffectDifference> found, Compilation compilation)
    {
        var differing = found.ToDictionary(static f => (IrNode)f.Node);
        foreach (var difference in found)
        {
            var inner = InnerDifferences(difference.Node, differing);
            if (inner.Count > 0 && Explains(inner, difference))
            {
                Interlocked.Increment(ref _outerExplainedByOperands);
                continue;
            }

            var reason = IrEffectDifferences.Reason(difference.Node, difference.Kind, difference.Old, difference.Ir,
                             compilation) ?? "unexplained, " + difference.Kind;
            _effectMismatches.GetOrAdd(reason, static _ => []).Add(difference.Message);
        }
    }

    // The differences of the nodes below node
    private static List<EffectDifference> InnerDifferences(IrNode node,
        Dictionary<IrNode, EffectDifference> differing) =>
        IrNodeChildren.Of(node)
            .SelectMany(static child => IrNodeChildren.DescendantsAndSelf(child))
            .Where(differing.ContainsKey)
            .Select(descendant => differing[descendant])
            .ToList();

    // Whether what the outer node adds over the syntax walk (or misses) is what its operands add (or miss): the IR's
    // outer effects lie within the syntax's plus the IR's inner ones, and the syntax's within the IR's plus the
    // syntax's inner ones
    private static bool Explains(List<EffectDifference> inner, EffectDifference outer)
    {
        var irInner = default(ExpressionEffects);
        var oldInner = default(ExpressionEffects);
        foreach (var difference in inner)
        {
            irInner = irInner.Union(difference.Ir);
            oldInner = oldInner.Union(difference.Old);
        }

        return IrEffectDifferences.IsWithin(outer.Ir, outer.Old.Union(irInner)) &&
               IrEffectDifferences.IsWithin(outer.Old, outer.Ir.Union(oldInner));
    }

    // The outermost expression whose span is span
    private static ExpressionSyntax? FindExpression(SyntaxNode root, TextSpan span)
    {
        var node = root.FindNode(span, getInnermostNodeForTie: true);
        if (node.Span != span || node is not ExpressionSyntax expression)
        {
            return null;
        }

        while (expression.Parent is ExpressionSyntax parent && parent.Span == span)
        {
            expression = parent;
        }

        return expression;
    }

    private static string? Kind(ExpressionEffects old, ExpressionEffects ir) =>
        (IrEffectDifferences.IsWithin(ir, old), IrEffectDifferences.IsWithin(old, ir)) switch
        {
            (true, true) => null,
            (true, false) => "IR finds less",
            (false, true) => "IR finds more",
            _ => "IR finds other"
        };

    private static string Describe(SyntaxAnnotation? annotation) =>
        annotation is null ? "none" : annotation.Kind + ":" + (annotation.Data ?? "");

    private sealed record EffectDifference(
        IrExpression Node,
        string Kind,
        ExpressionEffects Old,
        ExpressionEffects Ir,
        string Message);
}
