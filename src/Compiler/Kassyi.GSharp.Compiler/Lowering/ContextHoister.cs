namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Hoists local variables that cross execution barriers into a shared context struct.</summary>
/// <param name="copiedParameters">Parameters that live in the frame of a device function (see <see cref="LoweredStateMachine.CopiedParameters"/>).</param>
/// <param name="isDeviceFunction">
/// Whether the body belongs to a device function. Its shared memory is not set up by the launcher, so the initializer of
/// a hoisted <c>SharedMemory&lt;T&gt;</c> stays in the body.
/// </param>
public class ContextHoister(IReadOnlyCollection<string>? copiedParameters = null, bool isDeviceFunction = false)
    : CSharpSyntaxRewriter
{
    private readonly HashSet<string> _hoistedVariables = [];

    /// <summary>Analyzes barrier crossings and rewrites variable accesses to point to the hoisted context structure.</summary>
    public (BlockSyntax RewrittenBlock, IReadOnlyList<VariableDeclaratorSyntax> HoistedVariables) Hoist(
        BlockSyntax block)
    {
        _hoistedVariables.Clear();
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
            _hoistedVariables.UnionWith(copiedParameters);
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

        // [WHY] Accesses are rewritten by name, so a local that shares its name with a hoisted one (declared in a sibling
        // scope) reaches the context as well and needs a field of its own: its type may differ, and the transpilers name
        // each field after its declaration.
        analyzer.Variables
            .AsStackEnumerable()
            .Select(static variable => variable.Decl)
            .AddIf(hoistedDecls, hoistedNames, static (decl, names) => names.Contains(decl.Identifier.ValueText));
        _hoistedVariables.UnionWith(hoistedNames);

        var rewrittenBlock = (BlockSyntax)Visit(block);
        return (rewrittenBlock, hoistedDecls);
    }

    public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var remainingVariables = ImmutableArray.CreateBuilder<VariableDeclaratorSyntax>();
        var assignments = ImmutableArray.CreateBuilder<ExpressionStatementSyntax>();

        foreach (var variable in node.Declaration.Variables)
        {
            if (_hoistedVariables.Contains(variable.Identifier.ValueText))
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
            if (!_hoistedVariables.Contains(variable.Identifier.ValueText))
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
        if (_hoistedVariables.Contains(node.Identifier.ValueText))
        {
            return CreateContextAccess(node.Identifier, node).WithTriviaFrom(node);
        }

        return base.VisitIdentifierName(node);
    }

    // [WHY] The member name keeps the original span of the replaced node, so that the transpiler resolves it to the
    // hoisted local's symbol and emits the same C++ name as the context struct field. The original token keeps a
    // verbatim '@' for the managed C# fallback.
    private static MemberAccessExpressionSyntax CreateContextAccess(SyntaxToken identifier, SyntaxNode original)
    {
        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.PointerMemberAccessExpression,
            SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
            SyntaxFactory.IdentifierName(identifier.WithoutTrivia())
                .WithAdditionalAnnotations(
                    original.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind)
                )
        );
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
            var parentBlock = node.FirstAncestorOrSelf<BlockSyntax>();
            if (parentBlock != null)
            {
                Variables.Add((node, parentBlock.Span.End));
            }

            base.VisitVariableDeclarator(node);
        }
    }
}

