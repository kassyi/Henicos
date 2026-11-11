namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>The managed body that <see cref="ManagedExecutionRewriter"/> rewrites.</summary>
internal enum ManagedBodyKind
{
    /// <summary>The lowered body of a kernel's state machine.</summary>
    Kernel,

    /// <summary>The lowered body of the state machine of a device function that may suspend.</summary>
    SuspendingDeviceFunction,

    /// <summary>The user's body of a device function that does not suspend, copied into its managed version (#711).</summary>
    ManagedVersion
}

/// <summary>
/// Rewrites lowered device function calls and state machine constructs into managed execution equivalents.
/// </summary>
/// <param name="semanticModel">The model of the user's tree that the body comes from.</param>
/// <param name="bodyKind">What the body is emitted as.</param>
/// <param name="accessors">
/// The stubs through which the body calls an operator macro target it cannot name, and reaches the backing field of an
/// auto-property that an operator macro passes by reference.
/// </param>
/// <param name="isUnsafe">
/// Whether the context is an unsafe struct reached through a pointer rather than a class, so its members are read
/// with <c>-&gt;</c>.
/// </param>
internal sealed partial class ManagedExecutionRewriter(
    SemanticModel semanticModel,
    ManagedBodyKind bodyKind,
    ManagedAccessorSet accessors,
    bool isUnsafe = false) : CSharpSyntaxRewriter
{
    private readonly bool _isUnsafe = isUnsafe;

    private int _inLambdaOrLocalFuncCount;

    // The label after the Gpu.SingleThread body being rewritten, when the body has a return, and the lambda depth of
    // that body: a return at that depth jumps to the label (#693)
    private string? _singleThreadEndLabel;
    private int _singleThreadDepth;
    private int _nextSingleThreadId;

    // The number of the next loop variable that a Gpu.ForUnroll becomes; found on the first visit, which is the body
    private int _nextUnrollId = -1;

    // The number of the next hidden variable that a using (<variable>) statement declares
    private int _nextUsingResourceId;

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

    private SyntaxNode? VisitCreation(BaseObjectCreationExpressionSyntax node, SyntaxNode? visited)
    {
        if (TryRewriteSharedMemory(node, visited) is { } shared)
        {
            return shared;
        }

        if (TryWrapGenericStructConstructor(node, visited) is { } wrapped)
        {
            return wrapped;
        }

        return visited;
    }

    private SyntaxNode? TryRewriteSharedMemory(BaseObjectCreationExpressionSyntax node, SyntaxNode? visited)
    {
        if (visited is BaseObjectCreationExpressionSyntax rewritten &&
            semanticModel.GetTypeSymbol(node).IsSharedMemoryType())
        {
            return SharedMemorySiteKeyAnnotator.AddSharedMemorySiteKey(node, rewritten, semanticModel);
        }

        return null;
    }

    private SyntaxNode? TryWrapGenericStructConstructor(BaseObjectCreationExpressionSyntax node, SyntaxNode? visited)
    {
        // A generic struct constructor is monomorphized per closed struct type on CUDA and on the native CPU path (#543),
        // so the managed path wraps the creation in a type-key scope that salts any SharedMemory<T> declared inside it.
        if (visited is ExpressionSyntax rewrittenExpr &&
            semanticModel.GetTypeSymbol(node) is INamedTypeSymbol { TypeKind: TypeKind.Struct, IsGenericType: true } structType &&
            semanticModel.GetSymbol(node) is IMethodSymbol ctor &&
            StructConstructorEmitter.IsEmittedStructConstructor(ctor, semanticModel.Compilation) &&
            DeclaresSharedMemoryInStruct(structType, semanticModel.Compilation))
        {
            using var typeNameWriter = new SourceWriter();
            typeNameWriter.AppendCsharpTypeName(structType);
            var typeSyntax = SyntaxFactory.ParseTypeName(typeNameWriter.ToString());
            var typeArgs = SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(typeSyntax));
            var beginCall = SyntaxFactory.InvocationExpression(
                ManagedNames.CreateMemberAccess(
                    SyntaxFactory.GenericName(SyntaxFactory.Identifier("BeginTypeKeyScope"), typeArgs)
                )
            );
            // <context>.EndTypeKeyScope<<struct>>(<context>.BeginTypeKeyScope<<struct>>(), <creation>)
            // The type argument is spelled: a target-typed new(...) has no type of its own as an argument, so C# could
            // not infer it (CS0411, #1255)
            return SyntaxFactory.InvocationExpression(
                ManagedNames.CreateMemberAccess(
                    SyntaxFactory.GenericName(SyntaxFactory.Identifier("EndTypeKeyScope"), typeArgs)
                ),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList<ArgumentSyntax>(
                    [
                        SyntaxFactory.Argument(beginCall),
                        SyntaxFactory.Argument(rewrittenExpr.WithoutTrivia())
                    ])
                )
            ).WithTriviaFrom(rewrittenExpr);
        }

        return null;
    }

    private static bool DeclaresSharedMemoryInStruct(INamedTypeSymbol structType, Compilation compilation)
    {
        foreach (var syntaxRef in structType.OriginalDefinition.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is TypeDeclarationSyntax typeDecl)
            {
                var model = compilation.GetCachedSemanticModel(syntaxRef.SyntaxTree);
                foreach (var node in typeDecl.DescendantNodes())
                {
                    if (node is BaseObjectCreationExpressionSyntax creation &&
                        model.GetTypeSymbol(creation).IsSharedMemoryType())
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var visited = base.VisitMemberAccessExpression(node);
        if (TryRouteMemberAccess(node, visited) is { } routed)
        {
            return routed;
        }

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

        if (_isUnsafe)
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
        (node.Expression is IdentifierNameSyntax || ContextLocalRewriter.IsContextPointerAccess(node.Expression)) &&
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
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0)).WithTriviaFrom(node),
            // A grid sync request goes to the launcher whether a kernel or a device function makes it (#665):
            // global::Kassyi.GSharp.Core.ManagedGpuContext.GridSyncRequested
            GSharpInternalNames.StateMachine.YieldedForGridSync =>
                ManagedNames.CreateMemberAccess(ManagedNames.GridSyncRequestedName).WithTriviaFrom(node),
            // A block barrier the same way: global::Kassyi.GSharp.Core.ManagedGpuContext.BlockBarrierRequested
            GSharpInternalNames.StateMachine.YieldedForBlockBarrier =>
                ManagedNames.CreateMemberAccess(ManagedNames.BlockBarrierRequestedName).WithTriviaFrom(node),
            // __gsharp_ctx.<name> or __gsharp_ctx-><name>
            GSharpInternalNames.StateMachine.State =>
                SyntaxFactory.MemberAccessExpression(
                    _isUnsafe ? SyntaxKind.PointerMemberAccessExpression : SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
                    node
                ).WithTriviaFrom(node),
            // The generated file has none of the user's using directives
            _ => RouteOrQualify(node, base.VisitIdentifierName(node))
        };
    }

    // [WHY] A warp collective passes the frame it runs in (__gsharp_ctx) to the managed helpers, which read its path and
    // visit counts through IManagedWarpFrame (#1468). A reference lets the helper update the counts of an unsafe
    // context, a struct reached through a pointer, in place: ref __gsharp_ctx or ref *__gsharp_ctx
    public override SyntaxNode? VisitArgument(ArgumentSyntax node)
    {
        if (node.Expression is not IdentifierNameSyntax
            {
                Identifier.ValueText: GSharpInternalNames.StateMachine.Context
            } frame)
        {
            return base.VisitArgument(node);
        }

        ExpressionSyntax target = _isUnsafe
            ? SyntaxFactory.PrefixUnaryExpression(SyntaxKind.PointerIndirectionExpression, frame.WithoutTrivia())
            : frame.WithoutTrivia();
        return SyntaxFactory.Argument(target)
            .WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithTriviaFrom(node);
    }

    // [WHY] The copied bodies keep only the active branch of each #if; its directives and the disabled text would be
    // read again in the generated file, under its symbols (which define what a kept [Conditional] call needs,
    // RemovedCalls.GetSymbolsToDefine) and without the #endif or #endregion of a block that ends at a brace the copy
    // leaves out. Other directives (#pragma warning) stay as they were
    public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia) =>
        IsConditionalCompilationTrivia(trivia) ? default : base.VisitTrivia(trivia);

    private static bool IsConditionalCompilationTrivia(SyntaxTrivia trivia) =>
        trivia.Kind() is SyntaxKind.IfDirectiveTrivia or SyntaxKind.ElifDirectiveTrivia
            or SyntaxKind.ElseDirectiveTrivia or SyntaxKind.EndIfDirectiveTrivia or SyntaxKind.RegionDirectiveTrivia
            or SyntaxKind.EndRegionDirectiveTrivia or SyntaxKind.DisabledTextTrivia;

    public override SyntaxNode? VisitGenericName(GenericNameSyntax node) =>
        RouteOrQualify(node, base.VisitGenericName(node));

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

    // A lambda or local function whose expression body is a call that C# removes gets an empty body (#1540)
    private SyntaxNode? VisitNestedCallable<TNode>(TNode node, Func<TNode, SyntaxNode?> baseVisitor)
        where TNode : SyntaxNode
    {
        _inLambdaOrLocalFuncCount++;
        try
        {
            return RemovedCallEraser.WithEmptyBodyIfRemoved(node, baseVisitor(node), semanticModel);
        }
        finally
        {
            _inLambdaOrLocalFuncCount--;
        }
    }
}
