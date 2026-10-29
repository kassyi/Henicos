namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Rewrites lowered device function calls and state machine constructs into managed execution equivalents.
/// </summary>
internal sealed partial class ManagedExecutionRewriter(SemanticModel semanticModel, bool isDeviceFunction)
    : CSharpSyntaxRewriter
{
    /// <summary>
    /// The thread-static flag through which a managed state machine requests a grid sync from the managed launcher
    /// (<see cref="CpuHostLaunchEmitter"/>).
    /// </summary>
    internal const string ManagedGridSyncRequested = "global::Kassyi.GSharp.Core.ManagedGpuContext.GridSyncRequested";

    /// <summary>
    /// The thread-static flag through which a managed state machine tells the managed launcher that the thread waits at a
    /// block barrier (<see cref="CpuHostLaunchEmitter"/>).
    /// </summary>
    internal const string ManagedBlockBarrierRequested = "global::Kassyi.GSharp.Core.ManagedGpuContext.BlockBarrierRequested";

    private int _inLambdaOrLocalFuncCount;

    // The label after the Gpu.SingleThread body being rewritten, when the body has a return, and the lambda depth of
    // that body: a return at that depth jumps to the label (#693)
    private string? _singleThreadEndLabel;
    private int _singleThreadDepth;
    private int _nextSingleThreadId;

    // The number of the next loop variable that a Gpu.ForUnroll becomes; found on the first visit, which is the body
    private int _nextUnrollId = -1;

    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        if (_nextUnrollId < 0 && node is not null)
        {
            _nextUnrollId = FindNextUnrollId(node);
        }

        return base.Visit(node);
    }

    // A loop that PreLoweringExpander made of a barrier-bearing ForUnroll already uses these names. Its variable lives
    // in the context, so the two never clash; counting on from it only keeps the variables apart in the generated code
    private static int FindNextUnrollId(SyntaxNode root)
    {
        var next = 0;
        foreach (var token in root.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken) &&
                token.ValueText.StartsWith(GSharpInternalNames.Lowering.UnrollIndexPrefix, StringComparison.Ordinal) &&
                int.TryParse(
                    token.ValueText[GSharpInternalNames.Lowering.UnrollIndexPrefix.Length..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var id) &&
                id >= next)
            {
                next = id + 1;
            }
        }

        return next;
    }

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) =>
        VisitCreation(node, base.VisitObjectCreationExpression(node));

    public override SyntaxNode?
        VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node) =>
        VisitCreation(node, base.VisitImplicitObjectCreationExpression(node));

    private SyntaxNode? VisitCreation(BaseObjectCreationExpressionSyntax node, SyntaxNode? visited) =>
        visited is BaseObjectCreationExpressionSyntax rewritten &&
        semanticModel.GetTypeSymbol(node).IsSharedMemoryType()
            ? SharedMemorySiteKeyAnnotator.AddSharedMemorySiteKey(node, rewritten, semanticModel)
            : visited;

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var visited = base.VisitMemberAccessExpression(node);
        if (IsLengthOfOptionalArray(node) && visited is MemberAccessExpressionSyntax lengthAccess)
        {
            // (<array>?.Length ?? 0)
            return SyntaxFactory.ParenthesizedExpression(
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    SyntaxFactory.ConditionalAccessExpression(
                        lengthAccess.Expression.WithoutTrivia(),
                        SyntaxFactory.MemberBindingExpression(lengthAccess.Name.WithoutTrivia())
                    ),
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.NumericLiteralExpression,
                        SyntaxFactory.Literal(0))
                )
            ).WithTriviaFrom(lengthAccess);
        }

        if (!ContextLocalRewriter.IsContextPointerAccess(visited) ||
            visited is not MemberAccessExpressionSyntax memberAccess)
        {
            return visited;
        }

        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            memberAccess.Expression,
            memberAccess.Name).WithTriviaFrom(memberAccess);
    }

    // <parameter>.Length where the parameter is an optional array (T[]?) of the kernel that the kernel never writes
    // [WHY] The device reads a null optional array as one of length 0 (its data pointer is null, as `x is null` sees),
    // and the launchers pass it as null so that `x is null` stays true; the length is the one place that the managed
    // kernel must read as 0 instead of throwing (#806). Once the kernel assigns the parameter it may hold any null
    // array, whose Length throws on the device too (#706), so the rule is ArrayNullCheck's: an unwritten kernel
    // parameter only. A device function parameter is not rewritten either (the managed path does not carry where a null
    // came from, #852)
    private bool IsLengthOfOptionalArray(MemberAccessExpressionSyntax node) =>
        node.IsKind(SyntaxKind.SimpleMemberAccessExpression) &&
        node.Name is IdentifierNameSyntax { Identifier.ValueText: "Length" } &&
        node.Expression is IdentifierNameSyntax &&
        semanticModel.GetSymbol(node.Expression) is IParameterSymbol
        {
            Type: IArrayTypeSymbol, NullableAnnotation: NullableAnnotation.Annotated
        } parameter &&
        ArrayNullCheck.IsUnwrittenKernelParameter(parameter, semanticModel.Compilation);

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.Parent is MemberAccessExpressionSyntax parent && parent.Name == node)
        {
            return base.VisitIdentifierName(node);
        }

        return node.Identifier.Text switch
        {
            // The managed shuffle helpers keep the warp exchange themselves; any context will do
            GSharpInternalNames.SuspendingCall.Root =>
                SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context).WithTriviaFrom(node),
            // A grid sync request goes to the launcher whether a kernel or a device function makes it (#665):
            // global::Kassyi.GSharp.Core.ManagedGpuContext.GridSyncRequested
            GSharpInternalNames.StateMachine.YieldedForGridSync =>
                SyntaxFactory.ParseExpression(ManagedGridSyncRequested).WithTriviaFrom(node),
            // A block barrier the same way: global::Kassyi.GSharp.Core.ManagedGpuContext.BlockBarrierRequested
            GSharpInternalNames.StateMachine.YieldedForBlockBarrier =>
                SyntaxFactory.ParseExpression(ManagedBlockBarrierRequested).WithTriviaFrom(node),
            // __gsharp_ctx.<name>
            GSharpInternalNames.StateMachine.State
                or GSharpInternalNames.StateMachine.WarpExchange =>
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
                    node
                ).WithTriviaFrom(node),
            // The generated file has none of the user's using directives
            _ => ManagedNameQualifier.QualifyName(node, base.VisitIdentifierName(node), semanticModel)
        };
    }

    public override SyntaxNode? VisitGenericName(GenericNameSyntax node) =>
        ManagedNameQualifier.QualifyName(node, base.VisitGenericName(node), semanticModel);

    public override SyntaxNode? VisitInterpolation(InterpolationSyntax node) =>
        ManagedNameQualifier.ParenthesizeInterpolation(base.VisitInterpolation(node));

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) =>
        VisitNestedCallable(node, base.VisitLocalFunctionStatement);

    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) =>
        VisitNestedCallable(node, base.VisitSimpleLambdaExpression);

    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) =>
        VisitNestedCallable(node, base.VisitParenthesizedLambdaExpression);

    public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) =>
        VisitNestedCallable(node, base.VisitAnonymousMethodExpression);

    private SyntaxNode? VisitNestedCallable<TNode>(TNode node, Func<TNode, SyntaxNode?> baseVisitor)
    {
        _inLambdaOrLocalFuncCount++;
        try
        {
            return baseVisitor(node);
        }
        finally
        {
            _inLambdaOrLocalFuncCount--;
        }
    }
}
