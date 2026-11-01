using Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Connects all AST lowering passes into a pipeline that transforms standard C# kernel bodies into a flat state machine.</summary>
public static class AstLoweringPipeline
{
    /// <summary>Lowers the body of a kernel or device function, block or expression body, into a flat state machine.</summary>
    /// <param name="method">A method that has a body (see <see cref="MethodDeclarationSyntaxExtensions.HasBody"/>).</param>
    /// <param name="semanticModel">A semantic model of the compilation, used to find the calls that split the state machine.</param>
    /// <param name="owner">
    /// The symbol of <paramref name="method"/>. The value parameters it writes live in the frame
    /// (<see cref="LoweredStateMachine.CopiedParameters"/>), because a kernel and a device function are both resumed
    /// with their original arguments.
    /// </param>
    /// <param name="isDeviceFunction">Whether <paramref name="method"/> is a device function rather than a kernel.</param>
    public static LoweredStateMachine Lower(MethodDeclarationSyntax method, SemanticModel? semanticModel,
        IMethodSymbol? owner, bool isDeviceFunction)
    {
        var body = method.GetBodyNode()
                   ?? throw new InvalidOperationException("A method without a body cannot be lowered into a state machine.");
        var returnsValue = method.ReturnType is not PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword };
        return Lower(body, returnsValue, semanticModel, owner, isDeviceFunction);
    }

    /// <summary>Lowers a kernel block into a flat state machine block and extracts hoisted context fields.</summary>
    /// <param name="body">The kernel or device function body.</param>
    /// <param name="semanticModel">
    /// A semantic model of the compilation, used to find the calls that split the state machine (barriers, warp shuffles,
    /// calls that suspend). Without it, only the calls that already carry a split annotation split the state machine.
    /// </param>
    /// <param name="owner">
    /// The kernel or device function that declares <paramref name="body"/>, whose written value parameters live in the
    /// frame, or <see langword="null"/> when there is none (a body built by a test), which copies no parameter.
    /// </param>
    /// <param name="isDeviceFunction">Whether <paramref name="body"/> is the body of a device function rather than a kernel.</param>
    public static LoweredStateMachine Lower(BlockSyntax body, SemanticModel? semanticModel, IMethodSymbol? owner,
        bool isDeviceFunction) =>
        Lower(body, returnsValue: false, semanticModel, owner, isDeviceFunction);

    // owner is the method that declares body: the device function, or the kernel when its symbol is known
    private static LoweredStateMachine Lower(CSharpSyntaxNode body, bool returnsValue, SemanticModel? semanticModel,
        IMethodSymbol? owner, bool isDeviceFunction)
    {
        var bodyModel = ResolveBodyModel(body, semanticModel);

        // [WHY] Annotate before wrapping an expression body: the annotations record spans of the original tree, which is
        // how later passes find the symbols of the synthesized block.
        var splitPoints = CollectSplitPoints(body, bodyModel);
        var treeOrdinal = SemanticModelExtensions.GetSyntaxTreeOrdinal(bodyModel?.Compilation, body.SyntaxTree);
        var annotatedBody = ToBlock(new SpanAnnotationRewriter(splitPoints, treeOrdinal).Visit(body) ?? body, returnsValue);
        annotatedBody = PreLoweringExpander.Expand(annotatedBody, bodyModel);
        annotatedBody = LabeledStatementSplitter.Split(annotatedBody);

        // [WHY] A kernel is resumed with its original arguments, as a device function is (#879)
        var copiedParameters = owner != null
            ? FindAssignedValueParameters(body, owner, bodyModel)
            : ImmutableArray<string>.Empty;

        // [WHY] A loop header is evaluated at its own place in every iteration. The first round leaves headers alone;
        // once LoopRewriter has turned them into statements of the flat list, the second round lowers them there. Each
        // pass keeps one instance across both rounds so that temporaries and labels stay unique.
        var conditionalRewriter = new ConditionalEvaluationRewriter();
        var warpRewriter = new WarpInstructionRewriter(bodyModel);
        var branchRewriter = new BranchRewriter(bodyModel);
        var hoistedBody = conditionalRewriter.Rewrite(annotatedBody);
        hoistedBody = warpRewriter.Rewrite(hoistedBody);
        hoistedBody = branchRewriter.Rewrite(hoistedBody);
        hoistedBody = new ForEachRewriter(bodyModel).Rewrite(hoistedBody);
        hoistedBody = new LoopRewriter().Rewrite(hoistedBody);
        hoistedBody = conditionalRewriter.Rewrite(hoistedBody);
        hoistedBody = warpRewriter.Rewrite(hoistedBody);
        hoistedBody = branchRewriter.Rewrite(hoistedBody);

        // [WHY] Found once, before ContextHoister rewrites declarations, and given to both passes so that they agree
        var statePlan = StateLabelPlanner.Plan(hoistedBody, bodyModel);
        var hoister = new ContextHoister(copiedParameters, bodyModel, owner, statePlan);
        var (contextHoistedBody, contextFields) = hoister.Hoist(hoistedBody);
        if (!hoister.HasSplitPoint)
        {
            // [WHY] A body without a split point runs once and reads its parameters where they are
            copiedParameters = ImmutableArray<string>.Empty;
        }

        var lowerer = new StateMachineLowerer(isDeviceFunction, bodyModel, statePlan);
        var stateMachineBody = lowerer.Lower(contextHoistedBody);
        foreach (var copiedParameter in copiedParameters)
        {
            stateMachineBody = stateMachineBody.WithAdditionalAnnotations(
                new SyntaxAnnotation(LoweringAnnotations.CopiedParameterAnnotationKind, copiedParameter));
        }

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

    // [WHY] The caller (the launcher, for a kernel) passes the original arguments again on every resume, so a parameter
    // that the body changes has to live in the frame. A change is a write whose target starts at the parameter itself
    // (see MarkWrittenParameters) in one of the forms of TryGetWriteTarget, or an operator macro whose target takes the
    // parameter as its ref or out context (#944). With a semantic model the root is matched by symbol, so a lambda
    // parameter of the same name is not taken for it; without one, or when the model cannot bind the root, it is matched
    // by name (C# forbids a local that shadows a parameter), which can only copy too much.
    private static ImmutableArray<string> FindAssignedValueParameters(SyntaxNode body, IMethodSymbol method,
        SemanticModel? model)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        method.Parameters
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
            if (TryGetWriteTarget(node, model, out var target))
            {
                MarkWrittenParameters(target, method, model, candidates, assigned);
            }

            if (model != null &&
                node is AssignmentExpressionSyntax assignment &&
                OperatorMacroResolver.TryResolve(assignment, model, out var macro))
            {
                MarkWrittenContextParameters(macro, assignment, method, model, candidates, assigned);
            }
        }

        return assigned.ToImmutable();
    }

    // An operator macro whose target takes its context parameter by ref or out changes the context variable (#944)
    private static void MarkWrittenContextParameters(OperatorMacroResolver.OperatorMacro macro, AssignmentExpressionSyntax assignment,
        IMethodSymbol method, SemanticModel model, HashSet<string> candidates, ImmutableArray<string>.Builder assigned)
    {
        if (macro is { HasContext: true, TargetMethod: { } targetMethod } &&
            targetMethod.Parameters[0].RefKind is RefKind.Ref or RefKind.Out &&
            OperatorMacroContext.TryFindContextVariable(assignment, targetMethod, model, out var variable, out _) &&
            variable is IParameterSymbol parameter &&
            parameter.ContainingSymbol.OriginalDefinition.IsEqualTo(method.OriginalDefinition) &&
            candidates.Remove(parameter.Name))
        {
            assigned.Add(parameter.Name);
        }

        if (macro.Nested.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var nested in macro.Nested)
        {
            MarkWrittenContextParameters(nested.Inner, assignment, method, model, candidates, assigned);
        }
    }

    // The forms that write a variable, and the expression each one writes: an assignment (its left side; each element
    // of a deconstructing one, see MarkWrittenParameters), an increment or decrement, a ref / out argument, and the
    // receiver of a struct instance method that may change it. A new form of write goes into this list
    private static bool TryGetWriteTarget(SyntaxNode node, SemanticModel? model,
        [NotNullWhen(true)] out ExpressionSyntax? target)
    {
        target = node switch
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
        return target != null;
    }

    // [WHY] Moves every parameter that a write target may start at from candidates to assigned. A write changes the
    // storage of the variable its target starts at when every step from that variable reaches a part the variable holds
    // by value: a field (`v.X`, `v.A.B`) or an element of an [InlineArray] or fixed buffer (`v.Buffer[i]`). An array
    // element, or memory behind a pointer or a Span<T>, is not part of the variable, and a resume sees it again, so the
    // walk stops there. A tuple target writes each of its elements: (a, (v.X, b)) = ...; a conditional ref expression
    // may write either branch (#1000). Without a semantic model the walk follows the syntax only.
    private static void MarkWrittenParameters(ExpressionSyntax target, IMethodSymbol method,
        SemanticModel? model, HashSet<string> candidates, ImmutableArray<string>.Builder assigned)
    {
        switch (target)
        {
            case TupleExpressionSyntax tuple:
                foreach (var element in tuple.Arguments)
                {
                    MarkWrittenParameters(element.Expression, method, model, candidates, assigned);
                }

                return;
            case ParenthesizedExpressionSyntax parenthesized:
                MarkWrittenParameters(parenthesized.Expression, method, model, candidates, assigned);
                return;
            case RefExpressionSyntax refExpression:
                MarkWrittenParameters(refExpression.Expression, method, model, candidates, assigned);
                return;
            case ConditionalExpressionSyntax conditional:
                MarkWrittenParameters(conditional.WhenTrue, method, model, candidates, assigned);
                MarkWrittenParameters(conditional.WhenFalse, method, model, candidates, assigned);
                return;
            case MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } memberAccess
                when HoldsPartsByValue(memberAccess.Expression, model):
                MarkWrittenParameters(memberAccess.Expression, method, model, candidates, assigned);
                return;
            case ElementAccessExpressionSyntax elementAccess
                when HoldsPartsByValue(elementAccess.Expression, model):
                MarkWrittenParameters(elementAccess.Expression, method, model, candidates, assigned);
                return;
            case IdentifierNameSyntax root:
                var isParameter = model?.GetSymbol(root) switch
                {
                    null => true,
                    IParameterSymbol parameter =>
                        parameter.ContainingSymbol.OriginalDefinition.IsEqualTo(method.OriginalDefinition),
                    _ => false
                };
                var name = root.Identifier.ValueText;
                if (isParameter && candidates.Remove(name))
                {
                    assigned.Add(name);
                }

                return;
        }
    }

    // [WHY] A struct instance method that is not readonly receives its receiver by reference and may change it, and so
    // does an extension method whose `this` parameter is `ref` (`static void Bump(this ref Pt p)`, called as
    // `point.Bump()`); an `in` or `ref readonly` one cannot. Without a semantic model every receiver counts as changed.
    private static bool MayChangeReceiver(InvocationExpressionSyntax invocation, SemanticModel? model) =>
        model == null ||
        model.GetSymbol(invocation) switch
        {
            IMethodSymbol { MethodKind: MethodKind.ReducedExtension, ReducedFrom: { } extension } =>
                extension.Parameters is [{ RefKind: RefKind.Ref }, ..],
            IMethodSymbol
            {
                IsStatic: false,
                IsReadOnly: false,
                ContainingType: { IsValueType: true, IsReadOnly: false }
            } => true,
            _ => false
        };

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
