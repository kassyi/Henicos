namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Hoists local variables that cross execution barriers into a shared context struct.</summary>
/// <param name="copiedParameters">Parameters that live in the frame of a device function (see <see cref="LoweredStateMachine.CopiedParameters"/>).</param>
/// <param name="isDeviceFunction">
/// Whether the body belongs to a device function. Its shared memory is not set up by the launcher, so the initializer of
/// a hoisted <c>SharedMemory&lt;T&gt;</c> stays in the body.
/// </param>
/// <param name="semanticModel">
/// The semantic model of the method's syntax tree, used to key hoisted declarations and references by symbol and region
/// (<see cref="DeclarationKey"/>). When <see langword="null"/>, declarations and references are matched by name and region.
/// </param>
public class ContextHoister(
    IReadOnlyCollection<string>? copiedParameters = null,
    bool isDeviceFunction = false,
    SemanticModel? semanticModel = null)
    : CSharpSyntaxRewriter
{
    private readonly HashSet<DeclarationKey> _hoistedVariables = [];
    private readonly HashSet<string> _copiedParameters = [];

    /// <summary>Analyzes barrier crossings and rewrites variable accesses to point to the hoisted context structure.</summary>
    public (BlockSyntax RewrittenBlock, IReadOnlyList<VariableDeclaratorSyntax> HoistedVariables) Hoist(
        BlockSyntax block)
    {
        _hoistedVariables.Clear();
        _copiedParameters.Clear();
        var hoistedDecls = ImmutableArray.CreateBuilder<VariableDeclaratorSyntax>();

        // [WHY] One-pass AST traversal to collect barriers and variables efficiently in O(N).
        var analyzer = new HoistAnalyzer();
        analyzer.Visit(block);

        if (analyzer.BarrierSpans.Count == 0)
        {
            return (block, hoistedDecls);
        }

        if (copiedParameters != null)
        {
            _copiedParameters.UnionWith(copiedParameters);
        }

        var barrierCount = analyzer.BarrierSpans.Count;
        var barrierStarts = new int[barrierCount];
        for (var i = 0; i < barrierCount; i++)
        {
            barrierStarts[i] = analyzer.BarrierSpans[i].Start;
        }

        var hoistedNames = new HashSet<string>(StringComparer.Ordinal);
        // [WHY] Binary search (O(V log B)) is used to determine if variables cross any synchronization barriers.
        foreach (var (decl, scopeEnd) in analyzer.Variables)
        {
            var declEnd = decl.Span.End;

            var isSharedMemory = decl.Parent is VariableDeclarationSyntax
            {
                Type: GenericNameSyntax { Identifier.ValueText: TypeConstants.SharedMemoryType }
            };

            // The result of a call that may suspend is written when the callee completes, after any number of resumes
            var isSuspendingCallResult = decl.Initializer?.Value is InvocationExpressionSyntax initializerCall &&
                                         BarrierHelper.IsSuspendingCall(initializerCall);

            if (isSharedMemory || isSuspendingCallResult)
            {
                hoistedNames.Add(decl.Identifier.ValueText);
                continue;
            }

            // Binary search for the first barrier that comes after the declaration.
            var index = Array.BinarySearch(barrierStarts, declEnd);
            if (index < 0)
            {
                index = ~index;
            }

            // If a barrier is found after the declaration and before the end of the scope, it crosses.
            if (index < barrierCount && barrierStarts[index] < scopeEnd)
            {
                hoistedNames.Add(decl.Identifier.ValueText);
            }
        }

        // [WHY] Hoisting selection remains name-based so that sibling locals sharing a hoisted name still receive context
        // fields; rewrites then match each declaration and reference by its DeclarationKey (symbol + region).
        foreach (var (decl, _) in analyzer.Variables)
        {
            if (hoistedNames.Contains(decl.Identifier.ValueText))
            {
                hoistedDecls.Add(decl);
                _hoistedVariables.Add(LoweringIdentity.ForDeclaration(decl, semanticModel));
            }
        }

        var rewrittenBlock = (BlockSyntax)Visit(block);
        return (rewrittenBlock, hoistedDecls);
    }

    public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var remainingVariables = ImmutableArray.CreateBuilder<VariableDeclaratorSyntax>();
        var assignments = ImmutableArray.CreateBuilder<ExpressionStatementSyntax>();

        foreach (var variable in node.Declaration.Variables)
        {
            if (_hoistedVariables.Contains(LoweringIdentity.ForDeclaration(variable, semanticModel)))
            {
                // [WHY] Hoisted variables reside in the context struct, so initializations must become explicit member assignments.
                var typeSyntax = (variable.Parent as VariableDeclarationSyntax)?.Type;
                var isSharedMemory = typeSyntax is GenericNameSyntax
                {
                    Identifier.ValueText: TypeConstants.SharedMemoryType
                };

                if (variable.Initializer == null || (isSharedMemory && !isDeviceFunction))
                {
                    continue;
                }

                var assignment = SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    CreateContextAccess(variable.Identifier, variable),
                    (ExpressionSyntax)Visit(variable.Initializer.Value)
                );
                assignments.Add(SyntaxFactory.ExpressionStatement(assignment));
            }
            else if (base.VisitVariableDeclarator(variable) is VariableDeclaratorSyntax visitedVar)
            {
                remainingVariables.Add(visitedVar);
            }
        }

        if (remainingVariables.Count == 0)
        {
            return assignments.Count switch
            {
                0 => null,
                1 => assignments[0],
                _ => SyntaxFactory.Block(assignments)
            };
        }

        var newDeclaration = node.Declaration.WithVariables(SyntaxFactory.SeparatedList(remainingVariables));
        var newLocalDecl = node.WithDeclaration(newDeclaration);

        if (assignments.Count == 0)
        {
            return newLocalDecl;
        }

        // [WHY] Wraps separated assignments and remaining declarations in a block to preserve C# syntax validity.
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        statements.Add(newLocalDecl);
        statements.AddRange(assignments);
        return SyntaxFactory.Block(statements);
    }

    // [WHY] A loop without a barrier stays a for statement. When its variable is hoisted, the declaration would
    // initialize a local that no access reads, so the initializers assign the context field instead.
    // for (__gsharp_ctx-><variable> = <initializer>, ...; <condition>; <incrementors>) <body>
    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        var visited = (ForStatementSyntax?)base.VisitForStatement(node)
                      ?? throw new InvalidOperationException("Hoisting removed a for statement.");
        if (visited.Declaration is not { } declaration)
        {
            return visited;
        }

        var initializers = SyntaxFactory.SeparatedList<ExpressionSyntax>();
        var remainingVariables = SyntaxFactory.SeparatedList<VariableDeclaratorSyntax>();
        foreach (var variable in declaration.Variables)
        {
            if (!_hoistedVariables.Contains(LoweringIdentity.ForDeclaration(variable, semanticModel)))
            {
                remainingVariables = remainingVariables.Add(variable);
            }
            else if (variable.Initializer is { } initializer)
            {
                initializers = initializers.Add(SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    CreateContextAccess(variable.Identifier, variable),
                    initializer.Value
                ));
            }
        }

        if (remainingVariables.Count == declaration.Variables.Count)
        {
            return visited;
        }

        var loop = visited
            .WithDeclaration(null)
            .WithInitializers(initializers);
        if (remainingVariables.Count == 0)
        {
            return loop;
        }

        // { <type> <remaining variables>; <loop> }
        return SyntaxFactory.Block(
            SyntaxFactory.LocalDeclarationStatement(declaration.WithVariables(remainingVariables)),
            loop
        );
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (IsNonValueIdentifierPosition(node))
        {
            return base.VisitIdentifierName(node);
        }

        if (IsHoistedIdentifier(node))
        {
            return CreateContextAccess(node.Identifier, node).WithTriviaFrom(node);
        }

        return base.VisitIdentifierName(node);
    }

    // [WHY] Syntactic positions that name a member, type, label, or named argument/property rather than reading or
    // writing a local variable must never be rewritten, even when lowering runs without a SemanticModel (Tier 2).
    private static bool IsNonValueIdentifierPosition(IdentifierNameSyntax node) =>
        node.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name == node,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name == node,
            QualifiedNameSyntax => true,
            NameColonSyntax => true,
            NameEqualsSyntax => true,
            AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax } assignment => assignment.Left == node,
            GotoStatementSyntax => true,
            _ => false
        };

    private bool IsHoistedIdentifier(IdentifierNameSyntax node)
    {
        var key = LoweringIdentity.ForReference(node, semanticModel);
        if (_hoistedVariables.Contains(key))
        {
            return true;
        }

        if (!_copiedParameters.Contains(node.Identifier.ValueText))
        {
            return false;
        }

        if (semanticModel == null)
        {
            return true;
        }

        return key.Symbol is IParameterSymbol
        {
            ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Ordinary }
        };
    }

    // [WHY] The member name keeps the original span of the replaced node, so that the transpiler resolves it to the
    // hoisted local's symbol and emits the same C++ name as the context struct field. The outer MemberAccessExpression
    // also keeps the original span and any foreach receiver annotations so `GetTypeSymbol(node.Expression)` and
    // `node.Expression.HasAnnotations(...)` on an enclosing member access (`(__gsharp_ctx->arr).Length`) continue to see
    // the hoisted variable's original expression.
    // The original token keeps a verbatim '@' for the managed C# fallback.
    private static MemberAccessExpressionSyntax CreateContextAccess(SyntaxToken identifier, SyntaxNode original)
    {
        var spanAnnotations = original.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind);
        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.PointerMemberAccessExpression,
            SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
            SyntaxFactory.IdentifierName(identifier.WithoutTrivia())
                .WithAdditionalAnnotations(spanAnnotations)
        ).WithAdditionalAnnotations(spanAnnotations)
         .WithAdditionalAnnotations(original.GetAnnotations(LoweringAnnotations.ForEachArraySourceAnnotationKind));
    }

    private sealed class HoistAnalyzer : CSharpSyntaxWalker
    {
        public readonly ImmutableArray<TextSpan>.Builder BarrierSpans = ImmutableArray.CreateBuilder<TextSpan>();

        public readonly ImmutableArray<(VariableDeclaratorSyntax Decl, int ScopeEnd)>.Builder Variables =
            ImmutableArray.CreateBuilder<(VariableDeclaratorSyntax, int)>();

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (BarrierHelper.IsPhaseBarrier(node))
            {
                BarrierSpans.Add(node.Span);
            }

            base.VisitInvocationExpression(node);
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            // The resource of a using statement lives until the end of the statement. The analyzer rejects a barrier
            // inside it (KernelBarrierValidator), so it never crosses one; a barrier after it leaves it a plain local.
            if (node.Parent?.Parent is UsingStatementSyntax usingStatement)
            {
                Variables.Add((node, usingStatement.Span.End));
            }
            else if (node.FirstAncestorOrSelf<BlockSyntax>() is { } parentBlock)
            {
                Variables.Add((node, parentBlock.Span.End));
            }

            base.VisitVariableDeclarator(node);
        }
    }
}
