namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Connects all AST lowering passes into a pipeline that transforms standard C# kernel bodies into a flat state machine.</summary>
public static class AstLoweringPipeline
{
    /// <summary>Annotation kind that records the span of the original node a lowered node was derived from.</summary>
    public const string OriginalSpanAnnotationKind = "GSharpOriginalSpan";

    /// <summary>Annotation kind that marks a call to a device function that reaches a barrier (see <see cref="SuspensionAnalysis"/>).</summary>
    public const string SuspendingCallAnnotationKind = "GSharpSuspendingCall";

    /// <summary>
    /// Data of a <see cref="SuspendingCallAnnotationKind"/> annotation on a call whose callee returns a value. The state
    /// machine always stores that value, even when the caller discards it.
    /// </summary>
    public const string ValueReturningCallAnnotationData = "Value";

    /// <summary>Lowers the body of a kernel or device function, block or expression body, into a flat state machine.</summary>
    /// <param name="method">A method that has a body (see <see cref="MethodDeclarationSyntaxExtensions.HasBody"/>).</param>
    /// <param name="semanticModel">A semantic model of the compilation, used to find calls that suspend.</param>
    /// <param name="deviceFunction">The symbol of <paramref name="method"/> if it is a device function, or <see langword="null"/> for a kernel.</param>
    public static LoweredStateMachine Lower(MethodDeclarationSyntax method, SemanticModel? semanticModel,
        IMethodSymbol? deviceFunction = null)
    {
        var body = method.GetBodyNode()
                   ?? throw new InvalidOperationException("A method without a body cannot be lowered into a state machine.");
        var returnsValue = method.ReturnType is not PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword };
        return Lower(body, returnsValue, semanticModel, deviceFunction);
    }

    /// <summary>Lowers a kernel block into a flat state machine block and extracts hoisted context fields.</summary>
    /// <param name="body">The kernel or device function body.</param>
    /// <param name="semanticModel">
    /// A semantic model of the compilation, used to find calls that suspend. Without it, only barriers written in the
    /// body itself split the state machine.
    /// </param>
    /// <param name="deviceFunction">The device function that owns <paramref name="body"/>, or <see langword="null"/> for a kernel.</param>
    public static LoweredStateMachine Lower(BlockSyntax body, SemanticModel? semanticModel = null,
        IMethodSymbol? deviceFunction = null) =>
        Lower(body, returnsValue: false, semanticModel, deviceFunction);

    private static LoweredStateMachine Lower(CSharpSyntaxNode body, bool returnsValue, SemanticModel? semanticModel,
        IMethodSymbol? deviceFunction)
    {
        // [WHY] Annotate before wrapping an expression body: the annotations record spans of the original tree, which is
        // how later passes find the symbols of the synthesized block.
        var suspendingCalls = CollectSuspendingCalls(body, semanticModel);
        var annotatedBody = ToBlock(new SpanAnnotationRewriter(suspendingCalls).Visit(body) ?? body, returnsValue);

        var copiedParameters = deviceFunction != null
            ? FindAssignedValueParameters(body, deviceFunction)
            : ImmutableArray<string>.Empty;

        // [WHY] A loop header is evaluated at its own place in every iteration. The first hoisting pass leaves headers
        // alone; once LoopRewriter has turned them into statements of the flat list, the second pass hoists from there.
        var warpRewriter = new WarpInstructionRewriter();
        var hoistedBody = warpRewriter.Rewrite(annotatedBody);
        hoistedBody = new BranchRewriter().Rewrite(hoistedBody);
        hoistedBody = new LoopRewriter().Rewrite(hoistedBody);
        hoistedBody = warpRewriter.Rewrite(hoistedBody);

        var (contextHoistedBody, contextFields) =
            new ContextHoister(copiedParameters, isDeviceFunction: deviceFunction != null).Hoist(hoistedBody);
        var lowerer = new StateMachineLowerer(isDeviceFunction: deviceFunction != null);
        var stateMachineBody = lowerer.Lower(contextHoistedBody);

        return new LoweredStateMachine(stateMachineBody, contextFields, lowerer.CallSites, copiedParameters);
    }

    private static BlockSyntax ToBlock(SyntaxNode body, bool returnsValue) => body switch
    {
        BlockSyntax block => block,
        ArrowExpressionClauseSyntax { Expression: var expression } => SyntaxFactory.Block(returnsValue
            ? SyntaxFactory.ReturnStatement(expression)
            : SyntaxFactory.ExpressionStatement(expression)),
        _ => throw new InvalidOperationException($"Cannot lower a body of kind '{body.Kind()}'.")
    };

    // Maps the span of each call that suspends to whether its callee returns a value
    private static Dictionary<TextSpan, bool> CollectSuspendingCalls(SyntaxNode body, SemanticModel? semanticModel)
    {
        var calls = new Dictionary<TextSpan, bool>();
        if (semanticModel == null)
        {
            return calls;
        }

        var model = semanticModel.SyntaxTree == body.SyntaxTree
            ? semanticModel
            : semanticModel.Compilation.GetSemanticModel(body.SyntaxTree);
        foreach (var node in body.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation &&
                model.GetSymbolInfo(invocation).Symbol is IMethodSymbol callee &&
                SuspensionAnalysis.IsSuspendingFunction(callee, model.Compilation))
            {
                calls[invocation.Span] = !callee.ReturnsVoid;
            }
        }

        return calls;
    }

    // [WHY] The caller passes the original arguments again on every resume, so a parameter that the body changes has to
    // live in the frame. Parameters are matched by name: C# forbids a local that shadows a parameter.
    private static ImmutableArray<string> FindAssignedValueParameters(SyntaxNode body, IMethodSymbol deviceFunction)
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

    private sealed class SpanAnnotationRewriter(Dictionary<TextSpan, bool> suspendingCalls) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node == null)
            {
                return null;
            }

            var visited = (base.Visit(node) ?? node).WithAdditionalAnnotations(
                new SyntaxAnnotation(OriginalSpanAnnotationKind, $"{node.SpanStart},{node.Span.Length}"));

            if (node is not InvocationExpressionSyntax || !suspendingCalls.TryGetValue(node.Span, out var returnsValue))
            {
                return visited;
            }

            return visited.WithAdditionalAnnotations(returnsValue
                ? new SyntaxAnnotation(SuspendingCallAnnotationKind, ValueReturningCallAnnotationData)
                : new SyntaxAnnotation(SuspendingCallAnnotationKind));
        }
    }
}

/// <summary>Provides extension methods for chaining syntax rewriter passes on a block.</summary>
internal static class BlockSyntaxLoweringExtensions
{
    public static BlockSyntax LowerWith<T>(this BlockSyntax block) where T : CSharpSyntaxRewriter, new() =>
        (BlockSyntax)new T().Visit(block);
}
