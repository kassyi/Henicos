namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Renames the locals of a macro lambda body (<c>Gpu.GridStrideLoop</c>, <c>Gpu.ForUnroll</c>,
/// <c>Gpu.SingleThread</c>) that shadow a local or parameter in scope at the macro call, before the managed rewrite
/// copies the body into the enclosing method.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] C# 8 and later let a local of a lambda shadow a local or parameter of the method around it. Once the body is
/// a block of that method, the same declaration hides a name of an enclosing scope, which C# rejects (CS0136, #1254).
/// The C++ output is not affected: C++ lets an inner block hide a name, and every local there is named by its symbol.
/// </para>
/// <para>
/// A shadowing local takes its C++ name (<see cref="CppNameResolver"/>: the declaration ordinal, the name and the
/// region), as a hoisted local that shares its name does (<see cref="ManagedContextFields"/>); user identifiers cannot
/// contain <c>__</c> (GSH0055), so the name collides with nothing the user writes. Roslyn's Inline Method refactoring
/// settles the same conflict the same way, by renaming the inlined local rather than the caller's. Only the shadowing
/// locals are renamed, so the generated code keeps the user's names everywhere else. A local of a lambda or local
/// function nested in the body stays as it is: it remains in a function of its own, where shadowing is legal. A local
/// function that the body declares is renamed like a local.
/// </para>
/// </remarks>
internal static class ManagedShadowedLocals
{
    private static readonly StandardCppDialect s_cppDialect = new();

    /// <summary>
    /// The names of the locals, local functions and parameters in scope at the user's source of
    /// <paramref name="call"/>, the macro call (a node of the user's tree or of a body lowered from it); empty when the
    /// call maps to no source.
    /// </summary>
    /// <remarks>
    /// A local is in scope in its whole block, also before its declaration, so a local declared after the call counts
    /// too, as it does for CS0136.
    /// </remarks>
    public static HashSet<string> GetNamesInScope(SyntaxNode call, SemanticModel semanticModel)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (semanticModel.GetOriginalNode(call) is not { } original)
        {
            return names;
        }

        semanticModel.Compilation.GetCachedSemanticModel(original.SyntaxTree)
            .LookupSymbols(original.SpanStart)
            .AsStackEnumerable()
            .Where(static symbol => symbol is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol ||
                IsLocalFunction(symbol))
            .Select(static symbol => symbol.Name)
            .AddTo(names);
        return names;
    }

    /// <summary>
    /// <paramref name="body"/> (the body of a macro lambda) with each local or local function it declares whose name is
    /// in <paramref name="namesInScope"/> renamed, at its declaration and at every reference.
    /// </summary>
    public static TNode Rename<TNode>(TNode body, HashSet<string> namesInScope, SemanticModel semanticModel)
        where TNode : SyntaxNode
    {
        if (namesInScope.Count == 0)
        {
            return body;
        }

        var renames = new Dictionary<DeclarationKey, string>();
        foreach (var declaration in body.DescendantNodesAndSelf(static node => !IsNestedFunction(node)))
        {
            if (IsLocalDeclaration(declaration) &&
                namesInScope.Contains(GetDeclaredIdentifier(declaration).ValueText) &&
                LoweringIdentity.ForDeclaration(declaration, semanticModel) is { Symbol: { } local } key &&
                (local is ILocalSymbol || IsLocalFunction(local)))
            {
                renames[key] = CppNameResolver.Resolve(local, local.Name, s_cppDialect, key.Region).ToString();
            }
        }

        return renames.Count == 0
            ? body
            : new Rewriter(renames, semanticModel).Visit(body) as TNode
              ?? throw new InvalidOperationException("Renaming the shadowing locals of a macro body removed a node.");
    }

    private static bool IsLocalFunction(ISymbol symbol) =>
        symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction };

    // The declarations of a nested function are its own; the local function itself is a declaration of the body
    private static bool IsNestedFunction(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    private static bool IsLocalDeclaration(SyntaxNode node) =>
        node is VariableDeclaratorSyntax or SingleVariableDesignationSyntax or ForEachStatementSyntax
            or CatchDeclarationSyntax or LocalFunctionStatementSyntax;

    private static SyntaxToken GetDeclaredIdentifier(SyntaxNode declaration) => declaration switch
    {
        VariableDeclaratorSyntax declarator => declarator.Identifier,
        SingleVariableDesignationSyntax designation => designation.Identifier,
        ForEachStatementSyntax forEach => forEach.Identifier,
        CatchDeclarationSyntax catchDeclaration => catchDeclaration.Identifier,
        LocalFunctionStatementSyntax localFunction => localFunction.Identifier,
        _ => default
    };

    // Renames the declarations and references of the locals in <renames>, keyed as the lowering keys them (symbol and
    // region)
    private sealed class Rewriter(Dictionary<DeclarationKey, string> renames, SemanticModel semanticModel)
        : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            var visited = (VariableDeclaratorSyntax?)base.VisitVariableDeclarator(node);
            return visited is not null && TryGetName(node, out var name)
                ? visited.WithIdentifier(Rename(visited.Identifier, name))
                : visited;
        }

        public override SyntaxNode? VisitSingleVariableDesignation(SingleVariableDesignationSyntax node) =>
            TryGetName(node, out var name) ? node.WithIdentifier(Rename(node.Identifier, name)) : node;

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            var visited = (ForEachStatementSyntax?)base.VisitForEachStatement(node);
            return visited is not null && TryGetName(node, out var name)
                ? visited.WithIdentifier(Rename(visited.Identifier, name))
                : visited;
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            var visited = (LocalFunctionStatementSyntax?)base.VisitLocalFunctionStatement(node);
            return visited is not null && TryGetName(node, out var name)
                ? visited.WithIdentifier(Rename(visited.Identifier, name))
                : visited;
        }

        public override SyntaxNode? VisitCatchDeclaration(CatchDeclarationSyntax node) =>
            TryGetName(node, out var name) ? node.WithIdentifier(Rename(node.Identifier, name)) : node;

        // A member name binds to its member, never to a renamed local
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
            renames.TryGetValue(LoweringIdentity.ForReference(node, semanticModel), out var name)
                ? node.WithIdentifier(Rename(node.Identifier, name))
                : node;

        private bool TryGetName(SyntaxNode declaration, [NotNullWhen(true)] out string? name) =>
            renames.TryGetValue(LoweringIdentity.ForDeclaration(declaration, semanticModel), out name);

        private static SyntaxToken Rename(SyntaxToken identifier, string name) =>
            SyntaxFactory.Identifier(name).WithTriviaFrom(identifier);
    }
}
