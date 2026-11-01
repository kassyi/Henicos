namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Turns context accesses to the locals that are declared again on every resume (<see cref="ResumeLocals"/>) back into
/// locals, and renames the accesses to locals that share their
/// name with another hoisted local (see <see cref="ManagedContextFields"/>). Accesses are matched to declarations by
/// <see cref="DeclarationKey"/> (symbol and region). Labels that share their name with another label of the body are
/// renamed with their gotos (<c>__gsharp_label_&lt;ordinal&gt;_&lt;name&gt;</c>). The only entry is <see cref="Rewrite"/>,
/// which builds the label table from the whole body before it visits any part of it.
/// </summary>
internal sealed class ContextLocalRewriter : CSharpSyntaxRewriter
{
    private const string PinVariablePrefix = "__gsharp_pin_";

    private readonly IResumeLocalSet _resumeLocals;
    private readonly Dictionary<DeclarationKey, string> _renamedLocals;
    private readonly Dictionary<DeclarationKey, string> _labelRenames;
    private readonly SemanticModel _semanticModel;
    private readonly string _kernelName;
    private readonly bool _pinContextAddressOf;
    private int _pinCounter;

    private ContextLocalRewriter(
        IResumeLocalSet resumeLocals,
        Dictionary<DeclarationKey, string> renamedLocals,
        Dictionary<DeclarationKey, string> labelRenames,
        SemanticModel semanticModel,
        string kernelName,
        bool pinContextAddressOf)
    {
        _resumeLocals = resumeLocals;
        _renamedLocals = renamedLocals;
        _labelRenames = labelRenames;
        _semanticModel = semanticModel;
        _kernelName = kernelName;
        _pinContextAddressOf = pinContextAddressOf;
    }

    /// <summary>Rewrites the lowered <paramref name="body"/> of a kernel or device function for the managed output.</summary>
    /// <param name="body">The whole lowered body; the label renames are decided over all of it.</param>
    /// <param name="resumeLocals">The locals that are declared again on every resume, and their initialization.</param>
    /// <param name="renamedLocals">The new names of locals that share their name with another hoisted local.</param>
    /// <param name="semanticModel">The semantic model that keys declarations, references and labels.</param>
    /// <param name="kernelName">The name of the kernel or device function, for internal-error reports.</param>
    public static SyntaxNode? Rewrite(SyntaxNode body, IResumeLocalSet resumeLocals,
        Dictionary<DeclarationKey, string> renamedLocals, SemanticModel semanticModel, string kernelName) =>
        new ContextLocalRewriter(resumeLocals, renamedLocals, CreateLabelRenames(body, semanticModel), semanticModel,
            kernelName, pinContextAddressOf: true).Visit(body);

    /// <summary>
    /// Turns the context accesses of the resume locals of a native state machine back into locals; its labels stay as
    /// they are, as the C++ names come from the declarations.
    /// </summary>
    public static SyntaxNode? RewriteResumeLocals(SyntaxNode body, IResumeLocalSet resumeLocals,
        SemanticModel semanticModel, string kernelName) =>
        new ContextLocalRewriter(resumeLocals, new Dictionary<DeclarationKey, string>(),
            new Dictionary<DeclarationKey, string>(), semanticModel, kernelName, pinContextAddressOf: false).Visit(body);

    // [WHY] StateMachineLowerer keeps a label that no goto in another state reaches in its state, and lowering flattens
    // sibling scopes, so two labels of one name can end up in one block (CS0140) or one inside the other's block
    // (CS0158). Each label of a repeated name gets its own name; a goto finds its label by symbol (C# labels cannot
    // shadow each other, so the symbol is the one the source bound).
    private static Dictionary<DeclarationKey, string> CreateLabelRenames(SyntaxNode body, SemanticModel model)
    {
        // Lowering's own labels are unique by construction (a counter or a region in the name) and stay as they are
        var counts = LoweringIdentity.CountLabelNames(body, includeLoweringLabels: false);

        var renames = new Dictionary<DeclarationKey, string>();
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in body.DescendantNodesAndSelf())
        {
            if (node is not LabeledStatementSyntax labeled || LoweringIdentity.IsLoweringLabel(labeled) ||
                counts[labeled.Identifier.ValueText] < 2)
            {
                continue;
            }

            var name = labeled.Identifier.ValueText;
            var ordinal = ordinals.TryGetValue(name, out var previous) ? previous + 1 : 1;
            ordinals[name] = ordinal;
            renames[LoweringIdentity.ForDeclaration(labeled, model)] =
                $"{GSharpInternalNames.Lowering.ManagedLabelPrefix}{ordinal}_{name}";
        }

        return renames;
    }

    public override SyntaxNode? VisitLabeledStatement(LabeledStatementSyntax node)
    {
        var visited = (LabeledStatementSyntax?)base.VisitLabeledStatement(node) ?? node;
        return _labelRenames.TryGetValue(LoweringIdentity.ForDeclaration(node, _semanticModel), out var renamed)
            ? visited.WithIdentifier(SyntaxFactory.Identifier(renamed).WithTriviaFrom(visited.Identifier))
            : visited;
    }

    public override SyntaxNode? VisitGotoStatement(GotoStatementSyntax node)
    {
        var visited = (GotoStatementSyntax?)base.VisitGotoStatement(node) ?? node;
        return node is { RawKind: (int)SyntaxKind.GotoStatement, Expression: IdentifierNameSyntax target } &&
               _labelRenames.TryGetValue(LoweringIdentity.ForReference(target, _semanticModel), out var renamed)
            ? visited.WithExpression(SyntaxFactory.IdentifierName(renamed).WithTriviaFrom(target))
            : visited;
    }
    /// <summary>
    /// Matches <c>__gsharp_ctx-&gt;X</c>, the context access that <see cref="ContextHoister"/> emits.
    /// </summary>
    internal static bool IsContextPointerAccess(SyntaxNode? node) =>
        node is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.Text: GSharpInternalNames.StateMachine.Context }
        } memberAccess &&
        memberAccess.IsKind(SyntaxKind.PointerMemberAccessExpression);

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var visited = base.VisitMemberAccessExpression(node);
        if (!IsContextPointerAccess(visited) ||
            visited is not MemberAccessExpressionSyntax memberAccess)
        {
            return visited;
        }

        var key = LoweringIdentity.ForReference(memberAccess.Name, _semanticModel);
        if (key.Symbol == null)
        {
            return memberAccess;
        }

        SimpleNameSyntax name = _renamedLocals.TryGetValue(key, out var renamed)
            ? SyntaxFactory.IdentifierName(renamed)
            : memberAccess.Name;
        return _resumeLocals.Contains(key)
            ? name.WithTriviaFrom(memberAccess)
            : memberAccess.WithName(name);
    }

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (node.Expression is AssignmentExpressionSyntax
            {
                Left: MemberAccessExpressionSyntax memberAccess
            } assignment &&
            assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
            IsContextPointerAccess(memberAccess))
        {
            // Only the declarator-derived initialization resolves through GetDeclaredSymbol; later assignments stay.
            // The member name carries the declarator's span and region, so it keys like the declarator itself.
            var declaredKey = LoweringIdentity.ForDeclaration(memberAccess.Name, _semanticModel);
            if (declaredKey.Symbol is { } declaredSymbol && _resumeLocals.Contains(declaredKey) &&
                !PlacesDynamicSharedMemory(assignment.Right))
            {
                if (node.Parent is BlockSyntax or SwitchSectionSyntax)
                {
                    var value = Visit(assignment.Right) as ExpressionSyntax ?? assignment.Right;
                    SimpleNameSyntax name = _renamedLocals.TryGetValue(declaredKey, out var renamed)
                        ? SyntaxFactory.IdentifierName(renamed).WithAdditionalAnnotations(memberAccess.Name.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
                        : memberAccess.Name.WithoutTrivia();
                    return _resumeLocals.CreateInitialization(declaredKey, name, value)?.WithTriviaFrom(node);
                }

#pragma warning disable RS0030 // Statement text for a human-readable internal-error report only
                throw new InvalidOperationException(
                    $"Cannot remove ref-struct initialization statement in kernel '{_kernelName}' for local '{declaredSymbol.Name}': statement '{node.ToFullString().Trim()}' is not inside a block or switch section.");
#pragma warning restore RS0030
            }
        }

        return base.VisitExpressionStatement(node);
    }

    public override SyntaxNode? VisitBlock(BlockSyntax node)
    {
        var visited = (BlockSyntax?)base.VisitBlock(node) ?? node;
        return _pinContextAddressOf ? WrapBlockContextAddressOf(visited) : visited;
    }

    // [WHY] Taking `&` of a hoisted variable or copied parameter in the managed state machine takes the address of a
    // field of the heap-allocated context class (`&__gsharp_ctx.x`), which C# rejects with CS0212 outside a `fixed`
    // initializer (#958). Wrapping from the statement that takes `&__gsharp_ctx->...` up to the next label or the end of
    // the block pins the field for the rest of the state while keeping any local declared by the statement in scope.
    private BlockSyntax WrapBlockContextAddressOf(BlockSyntax block)
    {
        var statements = block.Statements;
        for (var i = 0; i < statements.Count; i++)
        {
            var pins = CollectUnfixedContextAddressOf(statements[i]);
            if (pins.IsEmpty)
            {
                continue;
            }

            var rewrittenStmt = ReplaceAddressOfWithPins(statements[i], pins);
            var endExclusive = i + 1;
            while (endExclusive < statements.Count && statements[endExclusive] is not LabeledStatementSyntax)
            {
                endExclusive++;
            }

            var innerStatements = ImmutableArray.CreateBuilder<StatementSyntax>(endExclusive - i);
            innerStatements.Add(rewrittenStmt);
            for (var j = i + 1; j < endExclusive; j++)
            {
                innerStatements.Add(statements[j]);
            }

            var wrappedTail = WrapBlockContextAddressOf(SyntaxFactory.Block(innerStatements.ToImmutable()));
            StatementSyntax current = wrappedTail;
            for (var p = pins.Length - 1; p >= 0; p--)
            {
                var (addressOf, pointerType, pinName) = pins[p];
                var declaration = SyntaxFactory.VariableDeclaration(
                    pointerType,
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(pinName))
                            .WithInitializer(SyntaxFactory.EqualsValueClause(addressOf.WithoutTrivia()))
                    )
                );
                current = SyntaxFactory.FixedStatement(
                    declaration,
                    current is BlockSyntax currentBlock ? currentBlock : SyntaxFactory.Block(current));
            }

            var outerStatements = ImmutableArray.CreateBuilder<StatementSyntax>(i + 1 + (statements.Count - endExclusive));
            for (var j = 0; j < i; j++)
            {
                outerStatements.Add(statements[j]);
            }

            outerStatements.Add(current);
            for (var j = endExclusive; j < statements.Count; j++)
            {
                outerStatements.Add(statements[j]);
            }

            return WrapBlockContextAddressOf(block.WithStatements(SyntaxFactory.List(outerStatements.ToImmutable())));
        }

        return block;
    }

    private ImmutableArray<(PrefixUnaryExpressionSyntax AddressOf, TypeSyntax PointerType, string PinName)>
        CollectUnfixedContextAddressOf(StatementSyntax statement)
    {
        var builder = ImmutableArray.CreateBuilder<(PrefixUnaryExpressionSyntax, TypeSyntax, string)>();
        foreach (var node in statement.DescendantNodes(static n =>
                     n is not (BlockSyntax or AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
        {
            if (node is not PrefixUnaryExpressionSyntax addressOf ||
                !addressOf.IsKind(SyntaxKind.AddressOfExpression) ||
                IsFixedInitializer(addressOf) ||
                !IsContextFieldLValue(addressOf.Operand) ||
                TryResolvePointedAtType(addressOf.Operand, _semanticModel) is not { } elementType)
            {
                continue;
            }

            using var writer = new SourceWriter();
            writer.AppendCsharpTypeName(elementType);
            writer.Append("*");
            var pointerType = SyntaxFactory.ParseTypeName(writer.ToString());
            var pinName = $"{PinVariablePrefix}{_pinCounter++}";
            builder.Add((addressOf, pointerType, pinName));
        }

        return builder.ToImmutable();
    }

    private static StatementSyntax ReplaceAddressOfWithPins(
        StatementSyntax statement,
        ImmutableArray<(PrefixUnaryExpressionSyntax AddressOf, TypeSyntax PointerType, string PinName)> pins) =>
        statement.ReplaceNodes(
            pins.AsStackEnumerable().Select(static p => p.AddressOf).ToArray(),
            (original, _) =>
            {
                foreach (var (addressOf, _, pinName) in pins)
                {
                    if (addressOf == original)
                    {
                        return SyntaxFactory.IdentifierName(pinName).WithTriviaFrom(original);
                    }
                }

                return original;
            });

    private static bool IsFixedInitializer(PrefixUnaryExpressionSyntax addressOf) =>
        addressOf.Parent is EqualsValueClauseSyntax
        {
            Parent: VariableDeclaratorSyntax
            {
                Parent: VariableDeclarationSyntax
                {
                    Parent: FixedStatementSyntax
                }
            }
        };

    private static bool IsContextFieldLValue(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case MemberAccessExpressionSyntax access when IsContextPointerAccess(access):
                    return true;
                case MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                    expression = access.Expression;
                    continue;
                default:
                    return false;
            }
        }
    }

    private static ITypeSymbol? TryResolvePointedAtType(ExpressionSyntax operand, SemanticModel semanticModel)
    {
        if (semanticModel.GetTypeSymbol(operand) is { TypeKind: not TypeKind.Error } type)
        {
            return type;
        }

        var target = operand is MemberAccessExpressionSyntax access && IsContextPointerAccess(access)
            ? access.Name
            : operand;
        return (semanticModel.GetSymbol(target) ?? semanticModel.GetDeclaredSymbol<ISymbol>(target)) switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            _ => null
        };
    }

    // The initialization of a dynamic SharedMemory<T> stays where it is, as an assignment to the local that each resume
    // declares: it places the region in the order the kernel reaches the declarations, as on CUDA (#544)
    private bool PlacesDynamicSharedMemory(ExpressionSyntax value) =>
        value is BaseObjectCreationExpressionSyntax creation &&
        _semanticModel.GetTypeSymbol(creation).IsSharedMemoryType() &&
        DynamicSharedMemoryAnalysis.TryGetCount(creation, _semanticModel, out _, out var constantCount) &&
        constantCount == null;
}
