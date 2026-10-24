namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Connects all AST lowering passes into a pipeline that transforms standard C# kernel bodies into a flat state machine.</summary>
public static class AstLoweringPipeline
{
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

        // [WHY] A loop header is evaluated at its own place in every iteration. The first round leaves headers alone;
        // once LoopRewriter has turned them into statements of the flat list, the second round lowers them there. Each
        // pass keeps one instance across both rounds so that temporaries and labels stay unique.
        var conditionalRewriter = new ConditionalEvaluationRewriter();
        var warpRewriter = new WarpInstructionRewriter();
        var branchRewriter = new BranchRewriter();
        var hoistedBody = conditionalRewriter.Rewrite(annotatedBody);
        hoistedBody = warpRewriter.Rewrite(hoistedBody);
        hoistedBody = branchRewriter.Rewrite(hoistedBody);
        hoistedBody = new LoopRewriter().Rewrite(hoistedBody);
        hoistedBody = conditionalRewriter.Rewrite(hoistedBody);
        hoistedBody = warpRewriter.Rewrite(hoistedBody);
        hoistedBody = branchRewriter.Rewrite(hoistedBody);

        var (contextHoistedBody, contextFields) =
            new ContextHoister(copiedParameters, isDeviceFunction: deviceFunction != null).Hoist(hoistedBody);
        var lowerer = new StateMachineLowerer(isDeviceFunction: deviceFunction != null);
        var stateMachineBody = lowerer.Lower(contextHoistedBody);

        return new LoweredStateMachine(stateMachineBody, contextFields, lowerer.CallSites, copiedParameters);
    }

    /// <summary>
    /// Lowers an iterator device function declaration into a <c>MoveNext()</c> state machine and extracts its struct
    /// fields.
    /// </summary>
    public static LoweredIterator LowerIterator(
        MethodDeclarationSyntax method,
        SemanticModel? semanticModel = null,
        IMethodSymbol? iterator = null)
    {
        var body = method.GetBodyNode()
                   ?? throw new InvalidOperationException(
                       "An iterator method without a body cannot be lowered into a state machine.");
        return LowerIterator(ToBlock(body, returnsValue: false), method.ParameterList, semanticModel, iterator);
    }

    /// <summary>
    /// Lowers an iterator block into a <c>MoveNext()</c> state machine and extracts its struct fields.
    /// </summary>
    public static LoweredIterator LowerIterator(
        BlockSyntax body,
        ParameterListSyntax? parameterList = null,
        SemanticModel? semanticModel = null,
        IMethodSymbol? iterator = null)
    {
        var annotatedBody = ToBlock(
            new SpanAnnotationRewriter(new Dictionary<TextSpan, bool>()).Visit(body) ?? body,
            returnsValue: false);

        var allParameters = CollectIteratorParameters(parameterList, iterator);
        var assignedParameters = FindAssignedParameters(body, allParameters);

        var forEachRewriter = new ForEachRewriter(LoweringMode.Iterator, semanticModel);
        var branchRewriter = new BranchRewriter(LoweringMode.Iterator);
        var hoistedBody = forEachRewriter.Rewrite(annotatedBody);
        hoistedBody = branchRewriter.Rewrite(hoistedBody);
        hoistedBody = new LoopRewriter(LoweringMode.Iterator).Rewrite(hoistedBody);
        hoistedBody = branchRewriter.Rewrite(hoistedBody);

        var (contextHoistedBody, contextFields) =
            new ContextHoister(allParameters, isDeviceFunction: true, mode: LoweringMode.Iterator)
                .Hoist(hoistedBody);
        var lowerer = new StateMachineLowerer(isDeviceFunction: false, mode: LoweringMode.Iterator);
        var moveNextBody = lowerer.Lower(contextHoistedBody);

        ITypeSymbol? elementType = null;
        if (iterator != null)
        {
            IteratorTypeResolver.IsGenericIEnumerable(iterator.ReturnType, out elementType);
        }

        return new LoweredIterator(moveNextBody, contextFields, assignedParameters, elementType);
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
        deviceFunction.Parameters
            .AsStackEnumerable()
            .Where(static parameter => parameter.RefKind == RefKind.None && !parameter.Type.IsRefLikeType)
            .Select(static parameter => parameter.Name)
            .AddTo(candidates);

        return FindAssignedParameters(body, candidates);
    }

    private static ImmutableArray<string> CollectIteratorParameters(
        ParameterListSyntax? parameterList,
        IMethodSymbol? iterator)
    {
        var parameters = ImmutableArray.CreateBuilder<string>();
        if (iterator != null)
        {
            iterator.Parameters
                .AsStackEnumerable()
                .Select(static parameter => parameter.Name)
                .AddTo(parameters);
        }
        else if (parameterList != null)
        {
            parameterList.Parameters
                .AsStackEnumerable()
                .Select(static parameter => parameter.Identifier.ValueText)
                .AddTo(parameters);
        }

        return parameters.ToImmutable();
    }

    private static ImmutableArray<string> FindAssignedParameters(
        SyntaxNode body,
        IReadOnlyCollection<string> candidateParameters)
    {
        if (candidateParameters.Count == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var candidates = new HashSet<string>(candidateParameters, StringComparer.Ordinal);
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

            var visited = (base.Visit(node)).WithAdditionalAnnotations(
                new SyntaxAnnotation(LoweringAnnotations.OriginalSpanAnnotationKind, $"{node.SpanStart},{node.Span.Length}"));

            if (node is not InvocationExpressionSyntax || !suspendingCalls.TryGetValue(node.Span, out var returnsValue))
            {
                return visited;
            }

            return visited.WithAdditionalAnnotations(returnsValue
                ? new SyntaxAnnotation(LoweringAnnotations.SuspendingCallAnnotationKind, LoweringAnnotations.ValueReturningCallAnnotationData)
                : new SyntaxAnnotation(LoweringAnnotations.SuspendingCallAnnotationKind));
        }
    }
}

/// <summary>Provides extension methods for chaining syntax rewriter passes on a block.</summary>
internal static class BlockSyntaxLoweringExtensions
{
    public static BlockSyntax LowerWith<T>(this BlockSyntax block) where T : CSharpSyntaxRewriter, new() =>
        (BlockSyntax)new T().Visit(block);
}
