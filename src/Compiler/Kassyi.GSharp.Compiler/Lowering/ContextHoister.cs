namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Hoists local variables that cross execution barriers into a shared context struct.</summary>
/// <param name="copiedParameters">Parameters that live in the frame of the method (see <see cref="LoweredStateMachine.CopiedParameters"/>).</param>
/// <param name="semanticModel">
/// The semantic model of the method's syntax tree, used to key hoisted declarations and references by symbol and region
/// (<see cref="DeclarationKey"/>). When <see langword="null"/>, declarations and references are matched by name and region.
/// </param>
/// <param name="method">
/// The kernel or device function that owns the body. With a semantic model, only references to its own parameters are
/// rewritten to the frame copies of <paramref name="copiedParameters"/>.
/// </param>
/// <remarks>
/// A local used after a label that starts a state (<see cref="StateLabelPlanner"/>), in another state than its
/// declaration, lives in the context like a local that crosses a barrier.
/// </remarks>
public class ContextHoister(
    IReadOnlyCollection<string>? copiedParameters = null,
    SemanticModel? semanticModel = null,
    IMethodSymbol? method = null)
    : CSharpSyntaxRewriter
{
    // The plan that AstLoweringPipeline gives this pass and StateMachineLowerer, or null to make one from the block
    private readonly StatePlan? _plan;

    // The ref locals of the method that no state after a split reads (RefLocalLiveness), which stay locals
    private readonly HashSet<ISymbol>? _refLocalsNotLiveAcrossSplit;

    /// <summary>Creates a hoister that takes the labels that start a state from <paramref name="plan"/>.</summary>
    /// <param name="copiedParameters">Parameters that live in the frame of the method.</param>
    /// <param name="semanticModel">The semantic model of the method's syntax tree.</param>
    /// <param name="method">The kernel or device function that owns the body.</param>
    /// <param name="plan">The labels that start a state.</param>
    /// <param name="refLocalsNotLiveAcrossSplit">
    /// The <c>ref</c> locals that are not live after any split point (<see cref="RefLocalLiveness"/>), which stay locals,
    /// or <see langword="null"/> to hoist every ref local that a barrier follows in its scope.
    /// </param>
    internal ContextHoister(IReadOnlyCollection<string>? copiedParameters, SemanticModel? semanticModel,
        IMethodSymbol? method, StatePlan plan, HashSet<ISymbol>? refLocalsNotLiveAcrossSplit = null)
        : this(copiedParameters, semanticModel, method)
    {
        _plan = plan;
        _refLocalsNotLiveAcrossSplit = refLocalsNotLiveAcrossSplit;
    }

    private readonly HashSet<DeclarationKey> _hoistedVariables = [];
    private readonly HashSet<string> _copiedParameters = [];

    // Marks the block of the statements that a split declaration became, which SpliceStatements removes again
    private static readonly SyntaxAnnotation s_spliceAnnotation = new("GSharpHoistedDeclarationSplice");

    /// <summary>
    /// Whether the block of the last <see cref="Hoist"/> has a split point. Without one the block is returned as it is, so
    /// neither its locals nor its copied parameters are moved into the context.
    /// </summary>
    internal bool HasSplitPoint { get; private set; }

    /// <summary>Analyzes barrier crossings and rewrites variable accesses to point to the hoisted context structure.</summary>
    public (BlockSyntax RewrittenBlock, IReadOnlyList<VariableDeclaratorSyntax> HoistedVariables) Hoist(
        BlockSyntax block)
    {
        _hoistedVariables.Clear();
        _copiedParameters.Clear();
        var hoistedDecls = ImmutableArray.CreateBuilder<VariableDeclaratorSyntax>();

        HasSplitPoint = HoistedVariableAnalysis.Analyze(block, semanticModel, _plan, _hoistedVariables, hoistedDecls,
            _refLocalsNotLiveAcrossSplit);
        if (!HasSplitPoint)
        {
            return (block, hoistedDecls);
        }

        if (copiedParameters != null)
        {
            _copiedParameters.UnionWith(copiedParameters);
        }

        var rewrittenBlock = (BlockSyntax)Visit(block);
        return (rewrittenBlock, hoistedDecls);
    }

    // [WHY] The declarators run in the order written, so a hoisted declarator's assignment stays in front of a later
    // declarator that reads it (#1257): the declaration is cut into declarations of the remaining declarators and
    // assignments of the hoisted ones, in the source order. The pieces replace the declaration in the enclosing
    // statement list (SpliceStatements), since a block around them would end the scope of the remaining declarators.
    public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var variables = node.Declaration.Variables;
        var visitedVariables = ImmutableArray.CreateBuilder<VariableDeclaratorSyntax>(variables.Count);
        foreach (var variable in variables)
        {
            visitedVariables.Add(
                (VariableDeclaratorSyntax?)base.VisitVariableDeclarator(variable)
                ?? throw new InvalidOperationException("Hoisting removed a variable declarator.")
            );
        }

        var statements = SplitDeclaration(
            node.Declaration,
            visitedVariables.MoveToImmutable(),
            node.WithDeclaration
        );
        return statements.Length switch
        {
            0 => null,
            1 => statements[0],
            _ => SyntaxFactory.Block(statements).WithAdditionalAnnotations(s_spliceAnnotation)
        };
    }

    public override SyntaxNode? VisitBlock(BlockSyntax node)
    {
        var visited = (BlockSyntax?)base.VisitBlock(node)
                      ?? throw new InvalidOperationException("Hoisting removed a block.");
        return visited.WithStatements(SpliceStatements(visited.Statements));
    }

    // [WHY] C# allows a label on a declaration (`L: int a = x, b = a + 1;`). The label moves to the first piece of a
    // split declaration, so that the enclosing statement list splices the pieces like those of an unlabeled one.
    // <label>: <first piece> <other pieces>
    public override SyntaxNode? VisitLabeledStatement(LabeledStatementSyntax node)
    {
        var visited = (LabeledStatementSyntax?)base.VisitLabeledStatement(node)
                      ?? throw new InvalidOperationException("Hoisting removed a labeled statement.");
        if (visited.Statement is not BlockSyntax block || !block.HasAnnotation(s_spliceAnnotation))
        {
            return visited;
        }

        var statements = block.Statements;
        return block.WithStatements(statements.Replace(statements[0], visited.WithStatement(statements[0])));
    }

    public override SyntaxNode? VisitSwitchSection(SwitchSectionSyntax node)
    {
        var visited = (SwitchSectionSyntax?)base.VisitSwitchSection(node)
                      ?? throw new InvalidOperationException("Hoisting removed a switch section.");
        return visited.WithStatements(SpliceStatements(visited.Statements));
    }

    // [WHY] A loop without a barrier stays a for statement. When its variable is hoisted, the declaration would
    // initialize a local that no access reads, so the initializers assign the context field instead. When some
    // declarators stay locals, all of them run in front of the loop in the order written, as in a declaration (#1257).
    // for (__gsharp_ctx-><variable> = <initializer>, ...; <condition>; <incrementors>) <body>
    // { <type> <remaining>; __gsharp_ctx-><variable> = <initializer>; ... for (; <condition>; <incrementors>) <body> }
    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        var visited = (ForStatementSyntax?)base.VisitForStatement(node)
                      ?? throw new InvalidOperationException("Hoisting removed a for statement.");
        if (visited.Declaration is not { } declaration)
        {
            return visited;
        }

        var variables = declaration.Variables;
        var hoistedCount = variables
            .AsStackEnumerable()
            .Count(this, static (variable, hoister) => hoister.IsHoistedDeclarator(variable));
        if (hoistedCount == 0)
        {
            return visited;
        }

        if (hoistedCount < variables.Count)
        {
            var statements = SplitDeclaration(
                declaration,
                ImmutableArray.CreateRange(variables),
                SyntaxFactory.LocalDeclarationStatement
            );
            return SyntaxFactory.Block(statements.Add(visited.WithDeclaration(null)));
        }

        var initializers = SyntaxFactory.SeparatedList<ExpressionSyntax>();
        foreach (var variable in variables)
        {
            if (variable.Initializer is { } initializer)
            {
                initializers = initializers.Add(AssignContextField(variable, initializer.Value));
            }
        }

        return visited
            .WithDeclaration(null)
            .WithInitializers(initializers);
    }

    private bool IsHoistedDeclarator(VariableDeclaratorSyntax variable) =>
        _hoistedVariables.Contains(LoweringIdentity.ForDeclaration(variable, semanticModel));

    // The declarators of declaration in the order written: each run of declarators that stay locals becomes one
    // declaration, each hoisted declarator with an initializer an assignment of its context field. visitedVariables are
    // the declarators with their initializers already rewritten, in the order of declaration.Variables.
    private ImmutableArray<StatementSyntax> SplitDeclaration(
        VariableDeclarationSyntax declaration,
        ImmutableArray<VariableDeclaratorSyntax> visitedVariables,
        Func<VariableDeclarationSyntax, StatementSyntax> createDeclaration)
    {
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        var remaining = ImmutableArray.CreateBuilder<VariableDeclaratorSyntax>();
        foreach (var variable in visitedVariables)
        {
            if (!IsHoistedDeclarator(variable))
            {
                remaining.Add(variable);
                continue;
            }

            FlushRemaining();

            // [WHY] Hoisted variables reside in the context struct, so initializations must become explicit member
            // assignments. A SharedMemory<T> declaration too, in a kernel as in a device function: the allocation runs
            // where the declaration is, so its count is evaluated there and a dynamic one is checked against
            // sharedMemBytes (#544)
            if (variable.Initializer is { } initializer)
            {
                statements.Add(SyntaxFactory.ExpressionStatement(AssignContextField(variable, initializer.Value)));
            }
        }

        FlushRemaining();
        return statements.ToImmutable();

        void FlushRemaining()
        {
            if (remaining.Count == 0)
            {
                return;
            }

            statements.Add(createDeclaration(declaration.WithVariables(SyntaxFactory.SeparatedList(remaining))));
            remaining.Clear();
        }
    }

    // __gsharp_ctx-><variable> = <value>
    private static AssignmentExpressionSyntax AssignContextField(VariableDeclaratorSyntax variable,
        ExpressionSyntax value) =>
        SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            CreateContextAccess(variable.Identifier, variable),
            value
        );

    // Replaces each block that VisitLocalDeclarationStatement made of a split declaration by its statements
    private static SyntaxList<StatementSyntax> SpliceStatements(SyntaxList<StatementSyntax> statements)
    {
        if (!statements
                .AsStackEnumerable()
                .Any(static statement => statement.HasAnnotation(s_spliceAnnotation)))
        {
            return statements;
        }

        var spliced = ImmutableArray.CreateBuilder<StatementSyntax>(statements.Count + 4);
        foreach (var statement in statements)
        {
            if (statement is BlockSyntax block && block.HasAnnotation(s_spliceAnnotation))
            {
                spliced.AddRange(block.Statements);
            }
            else
            {
                spliced.Add(statement);
            }
        }

        return SyntaxFactory.List(spliced);
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (HoistedVariableAnalysis.IsNonValueIdentifierPosition(node))
        {
            return base.VisitIdentifierName(node);
        }

        if (IsHoistedIdentifier(node))
        {
            return CreateContextAccess(node.Identifier, node).WithTriviaFrom(node);
        }

        return base.VisitIdentifierName(node);
    }

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

        // [WHY] Only the lowered method's own parameter lives in the frame. A parameter of the same name that belongs
        // to a lambda, a local function or a body that an expansion copied in (an inlined iterator) stays a plain
        // reference.
        return method != null &&
               key.Symbol is IParameterSymbol parameter &&
               parameter.ContainingSymbol.OriginalDefinition.IsEqualTo(method.OriginalDefinition);
    }

    // [WHY] The member name keeps the original span of the replaced node, so that the transpiler resolves it to the
    // hoisted local's symbol and emits the same C++ name as the context struct field. The outer MemberAccessExpression
    // also keeps the original span and any foreach receiver annotations so `GetTypeSymbol(node.Expression)` and
    // `node.Expression.HasAnnotations(...)` on an enclosing member access (`(__gsharp_ctx->arr).Length`) continue to see
    // the hoisted variable's original expression.
    // The original token keeps a verbatim '@' for the managed C# fallback.
    internal static MemberAccessExpressionSyntax CreateContextAccess(SyntaxToken identifier, SyntaxNode original)
    {
        var spanAnnotations = original.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind);
        var spilledTypeAnnotations = original.GetAnnotations(LoweringAnnotations.SpilledTypeSpanAnnotationKind);
        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.PointerMemberAccessExpression,
            SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
            SyntaxFactory.IdentifierName(identifier.WithoutTrivia())
                .WithAdditionalAnnotations(spanAnnotations)
                .WithAdditionalAnnotations(spilledTypeAnnotations)
        ).WithAdditionalAnnotations(spanAnnotations)
         .WithAdditionalAnnotations(spilledTypeAnnotations)
         .WithAdditionalAnnotations(original.GetAnnotations(LoweringAnnotations.ForEachArraySourceAnnotationKind));
    }
}
