namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Turns context accesses to the locals that are declared again on every resume (<see cref="ResumeLocals"/>) back into
/// locals, and renames the accesses to locals that share their
/// name with another hoisted local (see <see cref="ManagedContextFields"/>). Accesses are matched to declarations by
/// <see cref="DeclarationKey"/> (symbol and region). Labels that share their name with another label of the body are
/// renamed with their gotos (<c>__gsharp_label_&lt;ordinal&gt;_&lt;name&gt;</c>), and so are the local functions that
/// the state machine writes in front of its switch when another declaration of the body has their name, with their
/// calls (<c>__gsharp_localfn_&lt;ordinal&gt;_&lt;name&gt;</c>). The only entry is <see cref="Rewrite"/>, which builds
/// the label and function tables from the whole body before it visits any part of it.
/// </summary>
internal sealed class ContextLocalRewriter : CSharpSyntaxRewriter
{
    private readonly IResumeLocalSet _resumeLocals;
    private readonly Dictionary<DeclarationKey, string> _renamedLocals;
    private readonly Dictionary<DeclarationKey, string> _labelRenames;
    private readonly Dictionary<DeclarationKey, string> _localFunctionRenames;
    private readonly SemanticModel _semanticModel;
    private readonly string _kernelName;

    private ContextLocalRewriter(
        IResumeLocalSet resumeLocals,
        Dictionary<DeclarationKey, string> renamedLocals,
        Dictionary<DeclarationKey, string> labelRenames,
        Dictionary<DeclarationKey, string> localFunctionRenames,
        SemanticModel semanticModel,
        string kernelName)
    {
        _resumeLocals = resumeLocals;
        _renamedLocals = renamedLocals;
        _labelRenames = labelRenames;
        _localFunctionRenames = localFunctionRenames;
        _semanticModel = semanticModel;
        _kernelName = kernelName;
    }

    /// <summary>Rewrites the lowered <paramref name="body"/> of a kernel or device function for the managed output.</summary>
    /// <param name="body">The whole lowered body; the label renames are decided over all of it.</param>
    /// <param name="resumeLocals">The locals that are declared again on every resume, and their initialization.</param>
    /// <param name="renamedLocals">The new names of locals that share their name with another hoisted local.</param>
    /// <param name="semanticModel">The semantic model that keys declarations, references and labels.</param>
    /// <param name="kernelName">The name of the kernel or device function, for internal-error reports.</param>
    public static SyntaxNode? Rewrite(SyntaxNode body, IResumeLocalSet resumeLocals,
        Dictionary<DeclarationKey, string> renamedLocals, SemanticModel semanticModel, string kernelName) =>
        new ContextLocalRewriter(resumeLocals, renamedLocals, CreateLabelRenames(body, semanticModel),
            CreateLocalFunctionRenames(body, semanticModel), semanticModel, kernelName).Visit(body);

    /// <summary>
    /// Turns the context accesses of the resume locals of a native state machine back into locals; its labels stay as
    /// they are, as the C++ names come from the declarations.
    /// </summary>
    public static SyntaxNode? RewriteResumeLocals(SyntaxNode body, IResumeLocalSet resumeLocals,
        SemanticModel semanticModel, string kernelName) =>
        new ContextLocalRewriter(resumeLocals, new Dictionary<DeclarationKey, string>(),
            new Dictionary<DeclarationKey, string>(), new Dictionary<DeclarationKey, string>(), semanticModel,
            kernelName).Visit(body);

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

    // [WHY] StateMachineLowerer writes the local functions of a split body in front of its switch, where the sibling
    // scopes that lowering flattened meet: two functions of one name there are CS0128. A function whose name another
    // declaration of the body also has gets its own name, with its calls (a count that includes a lambda parameter of
    // the same name renames one function too many, which does no harm). The C++ names come from the symbols
    private static Dictionary<DeclarationKey, string> CreateLocalFunctionRenames(SyntaxNode body, SemanticModel model)
    {
        var renames = new Dictionary<DeclarationKey, string>();
        if (body is not BlockSyntax block)
        {
            return renames;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in body.DescendantNodes())
        {
            if (ManagedContextFields.GetDeclaredIdentifier(node) is { } identifier)
            {
                var declared = identifier.ValueText;
                counts[declared] = counts.TryGetValue(declared, out var count) ? count + 1 : 1;
            }
        }

        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var statement in block.Statements)
        {
            if (statement is not LocalFunctionStatementSyntax function ||
                counts[function.Identifier.ValueText] < 2)
            {
                continue;
            }

            var name = function.Identifier.ValueText;
            var ordinal = ordinals.TryGetValue(name, out var previous) ? previous + 1 : 1;
            ordinals[name] = ordinal;
            renames[LoweringIdentity.ForDeclaration(function, model)] =
                $"{GSharpInternalNames.Lowering.ManagedLocalFunctionPrefix}{ordinal}_{name}";
        }

        return renames;
    }

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        var visited = (LocalFunctionStatementSyntax?)base.VisitLocalFunctionStatement(node) ?? node;
        return _localFunctionRenames.Count > 0 &&
               _localFunctionRenames.TryGetValue(LoweringIdentity.ForDeclaration(node, _semanticModel), out var renamed)
            ? visited.WithIdentifier(SyntaxFactory.Identifier(renamed).WithTriviaFrom(visited.Identifier))
            : visited;
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
        _localFunctionRenames.Count > 0 && !HoistedVariableAnalysis.IsNonValueIdentifierPosition(node) &&
        _localFunctionRenames.TryGetValue(LoweringIdentity.ForReference(node, _semanticModel), out var renamed)
            ? node.WithIdentifier(SyntaxFactory.Identifier(renamed).WithTriviaFrom(node.Identifier))
            : base.VisitIdentifierName(node);

    public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
    {
        var visited = (GenericNameSyntax?)base.VisitGenericName(node) ?? node;
        return _localFunctionRenames.Count > 0 &&
               _localFunctionRenames.TryGetValue(LoweringIdentity.ForReference(node, _semanticModel), out var renamed)
            ? visited.WithIdentifier(SyntaxFactory.Identifier(renamed).WithTriviaFrom(visited.Identifier))
            : visited;
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

    public override SyntaxNode? VisitBreakStatement(BreakStatementSyntax node)
    {
        var visited = (BreakStatementSyntax?)base.VisitBreakStatement(node) ?? node;
        return TryGetRenamedJumpLabel(node, out var labelName, out var renamed)
            ? LabeledJumpSyntax.WithLabelName(
                visited,
                SyntaxFactory.IdentifierName(renamed).WithTriviaFrom(labelName))
            : visited;
    }

    public override SyntaxNode? VisitContinueStatement(ContinueStatementSyntax node)
    {
        var visited = (ContinueStatementSyntax?)base.VisitContinueStatement(node) ?? node;
        return TryGetRenamedJumpLabel(node, out var labelName, out var renamed)
            ? LabeledJumpSyntax.WithLabelName(
                visited,
                SyntaxFactory.IdentifierName(renamed).WithTriviaFrom(labelName))
            : visited;
    }

    private bool TryGetRenamedJumpLabel(
        StatementSyntax node,
        [NotNullWhen(true)] out IdentifierNameSyntax? labelName,
        [NotNullWhen(true)] out string? renamed)
    {
        renamed = null;
        if (LabeledJumpSyntax.GetLabelName(node) is not { } name)
        {
            labelName = null;
            return false;
        }

        labelName = name;
        var key = LabeledJumpSyntax.FindTarget(node)?.Parent is LabeledStatementSyntax target
            ? LoweringIdentity.ForDeclaration(target, _semanticModel)
            : LoweringIdentity.ForReference(name, _semanticModel);
        return _labelRenames.TryGetValue(key, out renamed);
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
            // The temporary of a suspending call has no symbol; it is a resume local when its type is a ref struct
            return memberAccess.Name.Identifier.ValueText.StartsWith(GSharpInternalNames.Lowering.CallTempPrefix,
                       StringComparison.Ordinal) && _resumeLocals.Contains(key)
                ? memberAccess.Name.WithTriviaFrom(memberAccess)
                : memberAccess;
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
                        ? SyntaxFactory.IdentifierName(renamed).WithOriginalSpanFrom(memberAccess.Name)
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

    public override SyntaxNode? VisitBlock(BlockSyntax node) =>
        (BlockSyntax?)base.VisitBlock(node) ?? node;

    // The initialization of a dynamic SharedMemory<T> stays where it is, as an assignment to the local that each resume
    // declares: it places the region in the order the kernel reaches the declarations, as on CUDA (#544)
    private bool PlacesDynamicSharedMemory(ExpressionSyntax value) =>
        value is BaseObjectCreationExpressionSyntax creation &&
        _semanticModel.GetTypeSymbol(creation).IsSharedMemoryType() &&
        DynamicSharedMemoryAnalysis.TryGetCount(creation, _semanticModel, out _, out var constantCount) &&
        constantCount == null;
}
