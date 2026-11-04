using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Consistently alpha-renames non-reserved parameters and local variables
/// in a target <c>[GpuKernel]</c> method to C++ keywords and CUDA/Prelude identifiers using
/// <see cref="SemanticModel"/> symbol resolution.
/// </summary>
internal static class MetamorphicIdentifierRenamer
{
    /// <summary>
    /// Identifiers that are valid unescaped identifiers in C# but are C++ keywords, CUDA built-in variables,
    /// or C/C++ standard library / G# Prelude symbols (stressing transpiler identifier escaping without triggering <c>GSH0055</c>).
    /// </summary>
    internal static IReadOnlyList<string> CandidateIdentifiers { get; } =
    [
        "template",
        "typename",
        "constexpr",
        "inline",
        "register",
        "mutable",
        "noexcept",
        "alignas",
        "alignof",
        "decltype",
        "nullptr",
        "reinterpret_cast",
        "static_cast",
        "const_cast",
        "dynamic_cast",
        "thread_local",
        "concept",
        "requires",
        "export",
        "threadIdx",
        "blockIdx",
        "blockDim",
        "gridDim",
        "warpSize",
        "syncthreads",
        "atomicAdd",
        "atomicSub",
        "atomicExch",
        "atomicMin",
        "atomicMax",
        "atomicCAS",
        "atomicAnd",
        "atomicOr",
        "atomicXor",
        "int32_t",
        "uint32_t",
        "int64_t",
        "uint64_t",
        "size_t",
        "ptrdiff_t",
    ];

    internal static CompilationUnitSyntax Apply(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        ulong seed,
        string? targetMethodName)
    {
        var method = MetamorphicTarget.FindKernel(root, targetMethodName);
        if (method?.Body is null || MetamorphicTypeResolver.HasSemanticErrorsInMethod(method, semanticModel))
        {
            return root;
        }

        var rng = new FuzzRandom(seed ^ 0xA11CE001UL);
        var usedNames = MetamorphicTarget.CollectIdentifiers(root);

        var targetSymbols = new List<ISymbol>();
        var seenSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var p in method.ParameterList.Parameters)
        {
            string name = p.Identifier.ValueText;
            if (!IsProtectedSpecialIdentifier(name) &&
                semanticModel.GetDeclaredSymbol(p) is IParameterSymbol paramSym &&
                seenSymbols.Add(paramSym))
            {
                targetSymbols.Add(paramSym);
            }
        }

        foreach (var node in method.Body.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax v:
                {
                    if (!IsProtectedSpecialIdentifier(v.Identifier.ValueText) &&
                        semanticModel.GetDeclaredSymbol(v) is ILocalSymbol localSym &&
                        seenSymbols.Add(localSym))
                    {
                        targetSymbols.Add(localSym);
                    }

                    break;
                }

                case SingleVariableDesignationSyntax desig:
                {
                    if (!IsProtectedSpecialIdentifier(desig.Identifier.ValueText) &&
                        semanticModel.GetDeclaredSymbol(desig) is ILocalSymbol localSym &&
                        seenSymbols.Add(localSym))
                    {
                        targetSymbols.Add(localSym);
                    }

                    break;
                }

                case ForEachStatementSyntax fe:
                {
                    if (!IsProtectedSpecialIdentifier(fe.Identifier.ValueText) &&
                        semanticModel.GetDeclaredSymbol(fe) is ILocalSymbol localSym &&
                        seenSymbols.Add(localSym))
                    {
                        targetSymbols.Add(localSym);
                    }

                    break;
                }
            }
        }

        if (targetSymbols.Count == 0)
        {
            return root;
        }

        var poolOrder = new List<string>(CandidateIdentifiers);
        ShuffleList(rng, poolOrder);
        int poolCursor = 0;

        var symbolRenameMap = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        foreach (var sym in targetSymbols)
        {
            string? chosenName = null;
            while (poolCursor < poolOrder.Count)
            {
                string candidate = poolOrder[poolCursor++];
                if (usedNames.Add(candidate))
                {
                    chosenName = candidate;
                    break;
                }
            }

            chosenName ??= MetamorphicTarget.AllocateFreshIdentifier("metaVar", usedNames);
            symbolRenameMap[sym] = chosenName;
        }

        var rewriter = new SemanticSymbolAlphaRenamer(semanticModel, symbolRenameMap);
        var updatedMethod = (MethodDeclarationSyntax)rewriter.Visit(method)!;
        return root.ReplaceNode(method, updatedMethod);
    }

    private static bool IsProtectedSpecialIdentifier(string name) =>
        name is "gid" or "hash" or "checksum";

    private static void ShuffleList<T>(FuzzRandom rng, List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.NextBelow(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private sealed class SemanticSymbolAlphaRenamer(
        SemanticModel semanticModel,
        IReadOnlyDictionary<ISymbol, string> symbolRenameMap) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitParameter(ParameterSyntax node)
        {
            var sym = semanticModel.GetDeclaredSymbol(node);
            var visited = (ParameterSyntax?)base.VisitParameter(node);
            if (visited is not null &&
                sym is not null &&
                symbolRenameMap.TryGetValue(sym, out string? replacement))
            {
                return visited.WithIdentifier(SyntaxFactory.Identifier(replacement).WithTriviaFrom(visited.Identifier));
            }

            return visited;
        }

        public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            var sym = semanticModel.GetDeclaredSymbol(node);
            var visited = (VariableDeclaratorSyntax?)base.VisitVariableDeclarator(node);
            if (visited is not null &&
                sym is not null &&
                symbolRenameMap.TryGetValue(sym, out string? replacement))
            {
                return visited.WithIdentifier(SyntaxFactory.Identifier(replacement).WithTriviaFrom(visited.Identifier));
            }

            return visited;
        }

        public override SyntaxNode? VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
        {
            var sym = semanticModel.GetDeclaredSymbol(node);
            var visited = (SingleVariableDesignationSyntax?)base.VisitSingleVariableDesignation(node);
            if (visited is not null &&
                sym is not null &&
                symbolRenameMap.TryGetValue(sym, out string? replacement))
            {
                return visited.WithIdentifier(SyntaxFactory.Identifier(replacement).WithTriviaFrom(visited.Identifier));
            }

            return visited;
        }

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            var sym = semanticModel.GetDeclaredSymbol(node);
            var visited = (ForEachStatementSyntax?)base.VisitForEachStatement(node);
            if (visited is not null &&
                sym is not null &&
                symbolRenameMap.TryGetValue(sym, out string? replacement))
            {
                return visited.WithIdentifier(SyntaxFactory.Identifier(replacement).WithTriviaFrom(visited.Identifier));
            }

            return visited;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (FuzzSyntaxHelpers.IsMemberAccessRight(node) ||
                (node.Parent is NameColonSyntax nameColon && nameColon.Name == node) ||
                (node.Parent is AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax } assign &&
                 assign.Left == node))
            {
                return node;
            }

            var sym = semanticModel.GetSymbolInfo(node).Symbol;
            if (sym is not null && symbolRenameMap.TryGetValue(sym, out string? replacement))
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(replacement).WithTriviaFrom(node.Identifier));
            }

            return base.VisitIdentifierName(node);
        }
    }
}
