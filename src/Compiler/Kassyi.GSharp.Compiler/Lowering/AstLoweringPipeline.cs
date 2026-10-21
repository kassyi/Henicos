namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Connects all AST lowering passes into a pipeline that transforms standard C# kernel bodies into a flat state machine.</summary>
public static class AstLoweringPipeline
{
    /// <summary>Annotation kind that records the span of the original node a lowered node was derived from.</summary>
    public const string OriginalSpanAnnotationKind = "GSharpOriginalSpan";

    /// <summary>Annotation kind that marks a call to a device function that reaches a barrier (see <see cref="SuspensionAnalysis"/>).</summary>
    public const string SuspendingCallAnnotationKind = "GSharpSuspendingCall";

    /// <summary>Lowers a kernel block into a flat state machine block and extracts hoisted context fields.</summary>
    /// <param name="body">The kernel or device function body.</param>
    /// <param name="semanticModel">
    /// A semantic model of the compilation, used to find calls that suspend. Without it, only barriers written in the
    /// body itself split the state machine.
    /// </param>
    /// <param name="deviceFunction">The device function that owns <paramref name="body"/>, or <see langword="null"/> for a kernel.</param>
    public static LoweredStateMachine Lower(BlockSyntax body, SemanticModel? semanticModel = null,
        IMethodSymbol? deviceFunction = null)
    {
        var suspendingCalls = CollectSuspendingCalls(body, semanticModel);
        var annotatedBody = (BlockSyntax)(new SpanAnnotationRewriter(suspendingCalls).Visit(body) ?? body);

        var copiedParameters = deviceFunction != null
            ? FindAssignedValueParameters(body, deviceFunction)
            : ImmutableArray<string>.Empty;

        var hoistedBody = new WarpInstructionRewriter().Rewrite(annotatedBody);
        hoistedBody = new BranchRewriter().Rewrite(hoistedBody);
        hoistedBody = new LoopRewriter().Rewrite(hoistedBody);

        var (contextHoistedBody, contextFields) =
            new ContextHoister(copiedParameters, isDeviceFunction: deviceFunction != null).Hoist(hoistedBody);
        var lowerer = new StateMachineLowerer(isDeviceFunction: deviceFunction != null);
        var stateMachineBody = lowerer.Lower(contextHoistedBody);

        return new LoweredStateMachine(stateMachineBody, contextFields, lowerer.CallSites, copiedParameters);
    }

    private static HashSet<TextSpan> CollectSuspendingCalls(BlockSyntax body, SemanticModel? semanticModel)
    {
        var spans = new HashSet<TextSpan>();
        if (semanticModel == null)
        {
            return spans;
        }

        var model = semanticModel.SyntaxTree == body.SyntaxTree
            ? semanticModel
            : semanticModel.Compilation.GetSemanticModel(body.SyntaxTree);
        foreach (var node in body.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation && SuspensionAnalysis.IsSuspendingCall(invocation, model))
            {
                spans.Add(invocation.Span);
            }
        }

        return spans;
    }

    // [WHY] The caller passes the original arguments again on every resume, so a parameter that the body changes has to
    // live in the frame. Parameters are matched by name: C# forbids a local that shadows a parameter.
    private static ImmutableArray<string> FindAssignedValueParameters(BlockSyntax body, IMethodSymbol deviceFunction)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in deviceFunction.Parameters)
        {
            if (parameter.RefKind == RefKind.None && !parameter.Type.IsRefLikeType)
            {
                candidates.Add(parameter.Name);
            }
        }

        var assigned = ImmutableArray.CreateBuilder<string>();
        foreach (var node in body.DescendantNodes())
        {
            var target = node switch
            {
                AssignmentExpressionSyntax assignment => assignment.Left,
                PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                                                        prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
                PostfixUnaryExpressionSyntax postfix => postfix.Operand,
                ArgumentSyntax { RefKindKeyword.RawKind: not (int)SyntaxKind.None } argument => argument.Expression,
                _ => null
            };

            if (target is IdentifierNameSyntax identifier &&
                candidates.Remove(identifier.Identifier.ValueText))
            {
                assigned.Add(identifier.Identifier.ValueText);
            }
        }

        return assigned.ToImmutable();
    }

    private sealed class SpanAnnotationRewriter(HashSet<TextSpan> suspendingCalls) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node == null)
            {
                return null;
            }

            var visited = (base.Visit(node) ?? node).WithAdditionalAnnotations(
                new SyntaxAnnotation(OriginalSpanAnnotationKind, $"{node.SpanStart},{node.Span.Length}"));

            return node is InvocationExpressionSyntax && suspendingCalls.Contains(node.Span)
                ? visited.WithAdditionalAnnotations(new SyntaxAnnotation(SuspendingCallAnnotationKind))
                : visited;
        }
    }
}

/// <summary>Provides extension methods for chaining syntax rewriter passes on a block.</summary>
internal static class BlockSyntaxLoweringExtensions
{
    public static BlockSyntax LowerWith<T>(this BlockSyntax block) where T : CSharpSyntaxRewriter, new() =>
        (BlockSyntax)new T().Visit(block);
}
