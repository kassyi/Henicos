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
internal sealed class IrClassificationEquivalence
{
    private readonly ConcurrentBag<string> _splitMismatches = [];
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _effectMismatches =
        new(StringComparer.Ordinal);
    private int _callsCompared;
    private int _splitCallsCompared;
    private int _invocationsInOpaque;
    private int _splitInvocationsInOpaque;
    private int _expressionsCompared;
    private int _expressionsWithOpaque;

    public IReadOnlyCollection<string> SplitMismatches => _splitMismatches;

    /// <summary>The effect mismatches by kind: the IR finds less, more, or neither contains the other.</summary>
    public IReadOnlyDictionary<string, ConcurrentBag<string>> EffectMismatches => _effectMismatches;

    public int CallsCompared => _callsCompared;

    public int SplitCallsCompared => _splitCallsCompared;

    public int InvocationsInOpaque => _invocationsInOpaque;

    public int SplitInvocationsInOpaque => _splitInvocationsInOpaque;

    public int ExpressionsCompared => _expressionsCompared;

    public int ExpressionsWithOpaque => _expressionsWithOpaque;

    /// <summary>Compares every GPU method of <paramref name="trees"/>.</summary>
    public void CompareAll(Compilation compilation, IEnumerable<SyntaxTree> trees)
    {
        foreach (var (declaration, symbol, built) in IrCoverageInputs.GpuMethods(compilation, trees))
        {
            var where = IrCoverageInputs.Where(declaration.SyntaxTree, symbol);
            var method = IrClassifier.Classify(built, compilation);
            CompareSplits(method, declaration, compilation, where);
            CompareEffects(method, declaration.SyntaxTree, compilation, where);
        }
    }

    // Every invocation outside the opaque nodes is a call of the IR with the split the lowering annotates, and every
    // call of the IR is an invocation
    private void CompareSplits(IrMethod method, MethodDeclarationSyntax declaration, Compilation compilation,
        string where)
    {
        var calls = new Dictionary<TextSpan, IrCall>();
        var opaque = new List<TextSpan>();
        IrNodeChildren.Visit(method.Body, node =>
        {
            switch (node)
            {
                case IrCall call:
                    calls[call.Source.Span] = call;
                    break;
                case IrOpaqueExpression expression:
                    opaque.Add(expression.Syntax.Span);
                    break;
                case IrOpaqueStatement statement:
                    opaque.Add(statement.Syntax.Span);
                    break;
            }
        });

        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        var body = (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody;
        var matched = 0;
        foreach (var invocation in body?.DescendantNodes().OfType<InvocationExpressionSyntax>() ?? [])
        {
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol callee)
            {
                continue;
            }

            var expected = Describe(AstLoweringPipeline.ClassifySplitPoint(callee, compilation));
            if (calls.TryGetValue(invocation.Span, out var call))
            {
                matched++;
                Interlocked.Increment(ref _callsCompared);
                var actual = Describe(call.Classification.Split, call.Method.ReturnsVoid);
                if (actual != "none")
                {
                    Interlocked.Increment(ref _splitCallsCompared);
                }

                if (actual != expected)
                {
                    _splitMismatches.Add($"{where}: {invocation}: lowering {expected}, IR {actual}");
                }
            }
            else if (opaque.Exists(span => span.Contains(invocation.Span)))
            {
                Interlocked.Increment(ref _invocationsInOpaque);
                if (expected != "none")
                {
                    Interlocked.Increment(ref _splitInvocationsInOpaque);
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
    }

    // The effects of the outermost node of each span that has the span of an expression of the source
    private void CompareEffects(IrMethod method, SyntaxTree tree, Compilation compilation, string where)
    {
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var seen = new HashSet<TextSpan>();
        var found =
            new List<(IrExpression Node, string Kind, string Message, ExpressionEffects Old, ExpressionEffects Ir)>();
        IrNodeChildren.Visit(method.Body, node =>
        {
            if (node is not IrExpression expression ||
                (expression.Source.Synthesized && expression is not IrConversion) ||
                !seen.Add(expression.Source.Span) ||
                FindExpression(root, expression.Source.Span) is not { } syntax)
            {
                return;
            }

            if (IrInvariants.ContainsOpaque(expression))
            {
                Interlocked.Increment(ref _expressionsWithOpaque);
                return;
            }

            Interlocked.Increment(ref _expressionsCompared);
            var isLocation = expression.Category is IrCategory.Location or IrCategory.ReadOnlyLocation;
            var old = isLocation ? ExpressionEffects.OfLocation(syntax, model) : ExpressionEffects.Of(syntax, model);
            var ir = isLocation ? IrEffects.OfLocation(expression, compilation) : IrEffects.Of(expression, compilation);
            if (Kind(old, ir) is { } kind)
            {
                found.Add((expression, kind,
                    $"{where}: {syntax} ({(isLocation ? "location" : "value")}): " +
                    $"syntax reads {old.Reads} writes {old.Writes}, IR reads {ir.Reads} writes {ir.Writes}", old, ir));
            }
        });

        // Only the innermost: an expression that holds a mismatching operand differs for the operand's reason
        var mismatching = new HashSet<IrNode>(found.Select(static f => (IrNode)f.Node));
        foreach (var (node, kind, message, old, ir) in found)
        {
            var inner = false;
            foreach (var child in IrNodeChildren.Of(node))
            {
                IrNodeChildren.Visit(child, descendant => inner |= mismatching.Contains(descendant));
            }

            if (!inner)
            {
                var reason = KnownDifference(node, kind, old, ir) ?? "unexplained, " + kind;
                _effectMismatches.GetOrAdd(reason, static _ => []).Add(message);
            }
        }
    }

    /// <summary>The reasons the IR and the syntax walk differ by design, each named by its prefix.</summary>
    public const string KnownPrefix = "known, ";

    // Where the IR follows the meaning and the syntax walk follows the shape (IrEffects remarks), or where the syntax
    // walk misses a call that the IR sees; null for a difference nobody explained
    private static string? KnownDifference(IrExpression node, string kind, ExpressionEffects old,
        ExpressionEffects ir)
    {
        var less = kind == "IR finds less";
        var more = kind == "IR finds more";
        if (node is IrConversion { IsImplicit: true, Operand.ConstantValue.HasValue: false } conversion &&
            (conversion.Conversion.Method is null || more))
        {
            // An implicit conversion has the span of its operand, which is not compared on its own; the difference
            // is the operand's (a user-defined conversion only adds reads, which both walks count)
            node = conversion.Operand;
        }

        return node switch
        {
            // The syntax walk takes the member an object initializer assigns as a location in memory
            IrAssignment { Target: IrFieldReference { Receiver: IrInitializedObject } } when less =>
                KnownPrefix + "an initializer writes the new value",
            // The syntax walk reads the location of a struct temporary (a call result, a creation, a nullable
            // unwrap) as memory
            IrFieldReference { Receiver: { Type.IsValueType: true } receiver } when less && !IsStorage(receiver) =>
                KnownPrefix + "a field of a struct temporary reads no memory",
            // The syntax walk takes a struct property (or indexer) as a location inside its receiver, without the
            // call of its getter
            IrFieldReference { Receiver: IrPropertyReference { Property.RefKind: RefKind.None } } when more =>
                KnownPrefix + "a struct property used as a receiver calls its getter",
            // The syntax walk takes a discard as a location in memory
            _ when less && HasDiscard(node) => KnownPrefix + "a discard is written by no one",
            // The syntax walk reads memory to find the location of this
            IrVariableReference { Variable.Kind: IrVariableKind.This } when less =>
                KnownPrefix + "the location of this reads nothing",
            // The syntax walk writes the variable of a by-value receiver of a reduced extension call
            IrCall { Method: { IsExtensionMethod: true, Parameters: [{ RefKind: RefKind.None }, ..] } }
                when less && old.Reads.IsSubsetOf(ir.Reads) =>
                KnownPrefix + "a by-value extension receiver is not written",
            // The syntax walk sees the parameters of the reduced method, without the receiver, and takes a pure
            // intrinsic extension of an array (AsSpan) as scalar math
            IrCall { Method.IsExtensionMethod: true } when more && ir.Reads.Memory && !old.Reads.Memory =>
                KnownPrefix + "an extension call is not scalar math",
            // The syntax walk does not see the call of a user-defined operator (ExpressionEffects ignores them)
            IrUnary { Operator.Method: not null } or IrBinary { Operator.Method: not null }
                or IrCompoundAssignment { Operator.Method: not null } or IrIncrement { OperatorMethod: not null }
                when more && ir.Writes.Memory && !old.Writes.Memory =>
                KnownPrefix + "a user-defined operator writes what its body writes",
            // The syntax walk skips a constant before its user-defined conversion (null to Span<T>)
            IrConversion { Conversion.Method: not null, Operand.ConstantValue.HasValue: true } when more =>
                KnownPrefix + "a user-defined conversion of a constant is a call",
            _ => null
        };
    }

    private static bool IsStorage(IrExpression expression) => expression switch
    {
        IrVariableReference or IrElementReference => true,
        IrFieldReference { Receiver: { } receiver } => !receiver.Type.IsValueType || IsStorage(receiver),
        IrFieldReference => true,
        IrPropertyReference property => property.Property.RefKind != RefKind.None,
        IrCall call => call.Method.RefKind != RefKind.None,
        IrConditional conditional => conditional.IsRef,
        _ => false
    };

    private static bool HasDiscard(IrExpression node)
    {
        var found = false;
        IrNodeChildren.Visit(node, child => found |= child is IrDiscard);
        return found;
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

    private static string? Kind(ExpressionEffects old, ExpressionEffects ir)
    {
        var irInOld = ir.Reads.IsSubsetOf(old.Reads) && ir.Writes.IsSubsetOf(old.Writes);
        var oldInIr = old.Reads.IsSubsetOf(ir.Reads) && old.Writes.IsSubsetOf(ir.Writes);
        return (irInOld, oldInIr) switch
        {
            (true, true) => null,
            (true, false) => "IR finds less",
            (false, true) => "IR finds more",
            _ => "IR finds other"
        };
    }

    private static string Describe(SyntaxAnnotation? annotation) =>
        annotation is null ? "none" : annotation.Kind + ":" + (annotation.Data ?? "");

    // The annotation the lowering would put on a call with this split
    private static string Describe(IrSplit split, bool returnsVoid) => split.Kind switch
    {
        IrSplitKind.None => "none",
        IrSplitKind.SuspendingCall => Describe(returnsVoid
            ? new SyntaxAnnotation(LoweringAnnotations.SuspendingCallAnnotationKind)
            : new SyntaxAnnotation(LoweringAnnotations.SuspendingCallAnnotationKind,
                LoweringAnnotations.ValueReturningCallAnnotationData)),
        IrSplitKind.BlockBarrier => Describe(LoweringAnnotations.CreateSyncIntrinsicAnnotation(
            new SyncIntrinsicInfo(SyncIntrinsicKind.BlockBarrier, SyncScope.Block, true, false))),
        IrSplitKind.GridBarrier => Describe(LoweringAnnotations.CreateSyncIntrinsicAnnotation(
            new SyncIntrinsicInfo(SyncIntrinsicKind.GridBarrier, SyncScope.Grid, true, false))),
        IrSplitKind.WarpShuffle => Describe(LoweringAnnotations.CreateSyncIntrinsicAnnotation(
            new SyncIntrinsicInfo(SyncIntrinsicKind.WarpShuffle, SyncScope.Warp, true, true, split.Shuffle))),
        _ => throw new ArgumentOutOfRangeException(nameof(split), split.Kind, null)
    };
}
