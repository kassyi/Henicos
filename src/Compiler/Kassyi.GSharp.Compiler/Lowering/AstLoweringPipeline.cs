namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Connects all AST lowering passes into a pipeline that transforms standard C# kernel bodies into a flat state machine.</summary>
public static class AstLoweringPipeline
{
    /// <summary>Lowers the body of a kernel or device function, block or expression body, into a flat state machine.</summary>
    /// <param name="method">A method that has a body (see <see cref="MethodDeclarationSyntaxExtensions.HasBody"/>).</param>
    /// <param name="semanticModel">A semantic model of the compilation, used to find the calls that split the state machine.</param>
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
    /// A semantic model of the compilation, used to find the calls that split the state machine (barriers, warp shuffles,
    /// calls that suspend). Without it, only the calls that already carry a split annotation split the state machine.
    /// </param>
    /// <param name="deviceFunction">The device function that owns <paramref name="body"/>, or <see langword="null"/> for a kernel.</param>
    public static LoweredStateMachine Lower(BlockSyntax body, SemanticModel? semanticModel = null,
        IMethodSymbol? deviceFunction = null) =>
        Lower(body, returnsValue: false, semanticModel, deviceFunction);

    private static LoweredStateMachine Lower(CSharpSyntaxNode body, bool returnsValue, SemanticModel? semanticModel,
        IMethodSymbol? deviceFunction)
    {
        var bodyModel = ResolveBodyModel(body, semanticModel);

        // [WHY] Annotate before wrapping an expression body: the annotations record spans of the original tree, which is
        // how later passes find the symbols of the synthesized block.
        var splitPoints = CollectSplitPoints(body, bodyModel);
        var treeOrdinal = SemanticModelExtensions.GetSyntaxTreeOrdinal(bodyModel?.Compilation, body.SyntaxTree);
        var annotatedBody = ToBlock(new SpanAnnotationRewriter(splitPoints, treeOrdinal).Visit(body) ?? body, returnsValue);
        annotatedBody = PreLoweringExpander.Expand(annotatedBody, bodyModel);
        annotatedBody = LabeledStatementSplitter.Split(annotatedBody);

        var copiedParameters = deviceFunction != null
            ? FindAssignedValueParameters(body, deviceFunction, bodyModel)
            : ImmutableArray<string>.Empty;

        // [WHY] A loop header is evaluated at its own place in every iteration. The first round leaves headers alone;
        // once LoopRewriter has turned them into statements of the flat list, the second round lowers them there. Each
        // pass keeps one instance across both rounds so that temporaries and labels stay unique.
        var conditionalRewriter = new ConditionalEvaluationRewriter();
        var warpRewriter = new WarpInstructionRewriter(bodyModel);
        var branchRewriter = new BranchRewriter();
        var hoistedBody = conditionalRewriter.Rewrite(annotatedBody);
        hoistedBody = warpRewriter.Rewrite(hoistedBody);
        hoistedBody = branchRewriter.Rewrite(hoistedBody);
        hoistedBody = new ForEachRewriter().Rewrite(hoistedBody);
        hoistedBody = new LoopRewriter().Rewrite(hoistedBody);
        hoistedBody = conditionalRewriter.Rewrite(hoistedBody);
        hoistedBody = warpRewriter.Rewrite(hoistedBody);
        hoistedBody = branchRewriter.Rewrite(hoistedBody);

        // [WHY] Found once, before ContextHoister rewrites declarations, and given to both passes so that they agree
        var statePlan = StateLabelPlanner.Plan(hoistedBody, bodyModel);
        var (contextHoistedBody, contextFields) =
            new ContextHoister(copiedParameters, isDeviceFunction: deviceFunction != null, bodyModel, deviceFunction,
                    statePlan)
                .Hoist(hoistedBody);
        var lowerer = new StateMachineLowerer(isDeviceFunction: deviceFunction != null, bodyModel, statePlan);
        var stateMachineBody = lowerer.Lower(contextHoistedBody);

        return new LoweredStateMachine(stateMachineBody, contextFields, lowerer.CallSites, copiedParameters);
    }

    private static SemanticModel? ResolveBodyModel(SyntaxNode body, SemanticModel? semanticModel) =>
        semanticModel == null
            ? null
            : semanticModel.SyntaxTree == body.SyntaxTree ||
              !semanticModel.Compilation.ContainsSyntaxTree(body.SyntaxTree)
                ? semanticModel
                : semanticModel.Compilation.GetSemanticModel(body.SyntaxTree);

    private static BlockSyntax ToBlock(SyntaxNode body, bool returnsValue) => body switch
    {
        BlockSyntax block => block,
        ArrowExpressionClauseSyntax { Expression: var expression } => SyntaxFactory.Block(returnsValue
            ? SyntaxFactory.ReturnStatement(expression)
            : SyntaxFactory.ExpressionStatement(expression)),
        _ => throw new InvalidOperationException($"Cannot lower a body of kind '{body.Kind()}'.")
    };

    // Maps the span of each call that splits the state machine to its annotation: a call to a device function that
    // suspends (whether its callee returns a value), or a synchronization intrinsic that splits the state machine,
    // decided by symbol however the call is spelled (#503). Lambda bodies are included: PreLoweringExpander decides with
    // the annotations whether a Gpu.ForUnroll body holds a split point. Without a semantic model nothing is annotated.
    private static Dictionary<TextSpan, SyntaxAnnotation> CollectSplitPoints(SyntaxNode body, SemanticModel? bodyModel)
    {
        var splitPoints = new Dictionary<TextSpan, SyntaxAnnotation>();
        if (bodyModel == null)
        {
            return splitPoints;
        }

        foreach (var node in body.DescendantNodes())
        {
            if (node is not InvocationExpressionSyntax invocation ||
                bodyModel.GetSymbol(invocation) is not IMethodSymbol callee)
            {
                continue;
            }

            if (SuspensionAnalysis.IsSuspendingFunction(callee, bodyModel.Compilation))
            {
                splitPoints[invocation.Span] = callee.ReturnsVoid
                    ? new SyntaxAnnotation(LoweringAnnotations.SuspendingCallAnnotationKind)
                    : new SyntaxAnnotation(LoweringAnnotations.SuspendingCallAnnotationKind,
                        LoweringAnnotations.ValueReturningCallAnnotationData);
            }
            else if (LoweringAnnotations.CreateSyncIntrinsicAnnotation(SyncIntrinsicClassifier.Classify(callee)) is { } sync)
            {
                splitPoints[invocation.Span] = sync;
            }
        }

        return splitPoints;
    }

    // [WHY] The caller passes the original arguments again on every resume, so a parameter that the body changes has to
    // live in the frame. A change is a write whose target starts at the parameter itself (see FindWrittenStorageRoot):
    // an assignment (each element of a deconstructing one), an increment or decrement, a ref / out argument, or the
    // receiver of a struct instance method that may change it. With a semantic model the root is matched by symbol, so
    // a lambda parameter of the same name is not taken for it; without one, or when the model cannot bind the root, it
    // is matched by name (C# forbids a local that shadows a parameter), which can only copy too much.
    private static ImmutableArray<string> FindAssignedValueParameters(SyntaxNode body, IMethodSymbol deviceFunction,
        SemanticModel? model)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        deviceFunction.Parameters
            .AsStackEnumerable()
            .Where(static parameter => parameter.RefKind == RefKind.None && !parameter.Type.IsRefLikeType)
            .Select(static parameter => parameter.Name)
            .AddTo(candidates);
        if (candidates.Count == 0)
        {
            return ImmutableArray<string>.Empty;
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
                InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax
                    {
                        RawKind: (int)SyntaxKind.SimpleMemberAccessExpression
                    } memberAccess
                } invocation when MayChangeReceiver(invocation, model) => memberAccess.Expression,
                _ => null
            };

            if (target != null)
            {
                MarkWrittenParameters(target, deviceFunction, model, candidates, assigned);
            }
        }

        return assigned.ToImmutable();
    }

    // Moves the parameter that a write target starts at from candidates to assigned. A tuple target writes each of its
    // elements: (a, (v.X, b)) = ...
    private static void MarkWrittenParameters(ExpressionSyntax target, IMethodSymbol deviceFunction,
        SemanticModel? model, HashSet<string> candidates, ImmutableArray<string>.Builder assigned)
    {
        if (target is TupleExpressionSyntax tuple)
        {
            foreach (var element in tuple.Arguments)
            {
                MarkWrittenParameters(element.Expression, deviceFunction, model, candidates, assigned);
            }

            return;
        }

        if (FindWrittenStorageRoot(target, model) is not { } root)
        {
            return;
        }

        var isParameter = model?.GetSymbol(root) switch
        {
            null => true,
            IParameterSymbol parameter =>
                parameter.ContainingSymbol.OriginalDefinition.IsEqualTo(deviceFunction.OriginalDefinition),
            _ => false
        };
        var name = root.Identifier.ValueText;
        if (isParameter && candidates.Remove(name))
        {
            assigned.Add(name);
        }
    }

    // [WHY] A struct instance method that is not readonly receives its receiver by reference and may change it. Without
    // a semantic model every receiver counts as changed.
    private static bool MayChangeReceiver(InvocationExpressionSyntax invocation, SemanticModel? model) =>
        model == null ||
        model.GetSymbol(invocation) is IMethodSymbol
        {
            IsStatic: false,
            IsReadOnly: false,
            ContainingType: { IsValueType: true, IsReadOnly: false }
        };

    // [WHY] A write changes the storage of the variable its target starts at when every step from that variable reaches
    // a part the variable holds by value: a field (`v.X`, `v.A.B`) or an element of an [InlineArray] or fixed buffer
    // (`v.Buffer[i]`). An array element, or memory behind a pointer or a Span<T>, is not part of the variable, and a
    // resume sees it again, so the walk stops there. Without a semantic model the walk follows the syntax only.
    private static IdentifierNameSyntax? FindWrittenStorageRoot(ExpressionSyntax target, SemanticModel? model)
    {
        var current = target;
        while (true)
        {
            switch (current)
            {
                case IdentifierNameSyntax identifier:
                    return identifier;
                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized.Expression;
                    break;
                case MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } memberAccess
                    when HoldsPartsByValue(memberAccess.Expression, model):
                    current = memberAccess.Expression;
                    break;
                case ElementAccessExpressionSyntax elementAccess
                    when HoldsPartsByValue(elementAccess.Expression, model):
                    current = elementAccess.Expression;
                    break;
                default:
                    return null;
            }
        }
    }

    // [WHY] A fixed buffer field has a pointer type but lies inside its struct. Any other pointer refers to memory
    // outside the variable; Roslyn reports a pointer type as a value type, so pointers are excluded before that test.
    private static bool HoldsPartsByValue(ExpressionSyntax receiver, SemanticModel? model)
    {
        if (model == null || model.GetSymbol(receiver) is IFieldSymbol { IsFixedSizeBuffer: true })
        {
            return true;
        }

        return model.GetTypeSymbol(receiver) switch
        {
            null => true,
            IPointerTypeSymbol => false,
            var type => type is { IsValueType: true, IsRefLikeType: false }
        };
    }

    private sealed class SpanAnnotationRewriter(Dictionary<TextSpan, SyntaxAnnotation> splitPoints, int treeOrdinal) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node == null)
            {
                return null;
            }

            var visited = base.Visit(node);
            if (!node.HasAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
            {
                visited = visited.WithAdditionalAnnotations(
                    new SyntaxAnnotation(
                        LoweringAnnotations.OriginalSpanAnnotationKind,
                        LoweringAnnotations.FormatOriginalSpanData(node, treeOrdinal)));
            }

            return node is InvocationExpressionSyntax && splitPoints.TryGetValue(node.Span, out var splitPoint)
                ? visited.WithAdditionalAnnotations(splitPoint)
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
