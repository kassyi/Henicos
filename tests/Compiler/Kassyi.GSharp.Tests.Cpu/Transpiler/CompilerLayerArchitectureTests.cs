#nullable enable
using System.Globalization;
using System.Text;
using Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

namespace Kassyi.GSharp.Tests.Cpu.Transpiler;

/// <summary>
/// Enforces the #2314 compiler layering gate: <c>Ir/</c> (excluding <c>Ir/Linear/IrStateMachine*</c>),
/// <c>Semantics/</c>, <c>Dialects/</c>, and <c>Backends/</c> must not introduce new direct or indirect references
/// to types or extension methods declared in <c>Transpiler/</c> or <c>Lowering/</c>.
/// </summary>
[Collection(TestCollections.CorpusScan)]
public class CompilerLayerArchitectureTests(ITestOutputHelper output)
{
    private const string DirectExpectedFile = "CompilerLayerDirect.expected.yml";
    private const string IndirectExpectedFile = "CompilerLayerIndirect.expected.yml";

    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Lazy<LayerGraph> s_layerGraph = new(BuildLayerGraph, LazyThreadSafetyMode.ExecutionAndPublication);

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void CompilerLayers_DirectReferences_MatchExpected()
    {
        var graph = s_layerGraph.Value;
        Assert.Empty(graph.CompilationErrors);

        AssertEdgesMatchExpected(
            name: "CompilerLayerDirect",
            expectedFile: DirectExpectedFile,
            headerComment:
                "# Direct references from Ir/ (excluding Ir/Linear/IrStateMachine*), Semantics/, Dialects/, and Backends/\r\n" +
                "# to types or extension methods declared in Transpiler/ or Lowering/.",
            actualEdges: graph.DirectEdges);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void CompilerLayers_IndirectReferences_MatchExpected()
    {
        var graph = s_layerGraph.Value;
        Assert.Empty(graph.CompilationErrors);

        AssertEdgesMatchExpected(
            name: "CompilerLayerIndirect",
            expectedFile: IndirectExpectedFile,
            headerComment:
                "# Indirect references: types reachable from Tier 1 (excluding Transpiler/, Lowering/, and Ir/Linear/IrStateMachine*)\r\n" +
                "# that reference types or extension methods declared in Transpiler/ or Lowering/.",
            actualEdges: graph.IndirectEdges);
    }

    private void AssertEdgesMatchExpected(
        string name,
        string expectedFile,
        string headerComment,
        ImmutableArray<string> actualEdges)
    {
        var expectedPath = RepositoryPaths.Combine(
            "tests",
            "Compiler",
            "Kassyi.GSharp.Tests.Cpu",
            "Transpiler",
            expectedFile);

        var expectedExists = File.Exists(expectedPath);
        var expectedEdges = ReadExpectedEdges(expectedPath);

        var added = actualEdges
            .Where(edge => !expectedEdges.Contains(edge))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var actualSet = actualEdges.ToHashSet(StringComparer.Ordinal);
        var removed = expectedEdges
            .Where(edge => !actualSet.Contains(edge))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (expectedExists && added.Length == 0 && removed.Length == 0)
        {
            return;
        }

        var receivedPath = IrExpectedFiles.ReceivedPath(name, ".yml");
        var yaml = FormatYamlDocument(headerComment, actualEdges);
        File.WriteAllText(receivedPath, yaml, s_utf8NoBom);
        output.WriteLine("Received: " + receivedPath);

        var message = new StringBuilder();
        if (!expectedExists)
        {
            message.Append("Missing expected file: ").Append(expectedPath).AppendLine();
        }

        if (added.Length > 0)
        {
            message.Append("Added edges (").Append(added.Length).AppendLine("):");
            foreach (var edge in added)
            {
                message.Append("  + ").AppendLine(edge);
            }
        }

        if (removed.Length > 0)
        {
            message.Append("Removed edges (").Append(removed.Length).AppendLine("):");
            foreach (var edge in removed)
            {
                message.Append("  - ").AppendLine(edge);
            }
        }

        message.Append("Received: ").Append(receivedPath);
        Assert.Fail(message.ToString());
    }

    private static string FormatYamlDocument(string headerComment, ImmutableArray<string> edges)
    {
        var builder = new StringBuilder();
        builder.Append(headerComment).Append("\r\n");
        foreach (var edge in edges)
        {
            builder.Append("- \"");
            foreach (var ch in edge)
            {
                if (ch is '\\' or '"')
                {
                    builder.Append('\\');
                }

                builder.Append(ch);
            }

            builder.Append("\"\r\n");
        }

        return builder.ToString();
    }

    private static HashSet<string> ReadExpectedEdges(string expectedPath)
    {
        var edges = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(expectedPath))
        {
            return edges;
        }

        foreach (var rawLine in File.ReadAllLines(expectedPath))
        {
            var span = rawLine.AsSpan().Trim();
            if (span.IsEmpty || span[0] == '#')
            {
                continue;
            }

            if (!span.StartsWith("- ", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unexpected YAML line in {expectedPath}: {rawLine}");
            }

            var value = span[2..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            edges.Add(UnescapeYamlQuoted(value));
        }

        return edges;
    }

    private static string UnescapeYamlQuoted(ReadOnlySpan<char> value)
    {
        if (!value.Contains('\\'))
        {
            return value.ToString();
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    private static LayerGraph BuildLayerGraph()
    {
        var compilation = RoslynTestUtility.CreateCompilerFullSourceCompilation();
        var compilationErrorList = new List<string>();
        foreach (var diagnostic in compilation.GetDeclarationDiagnostics())
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                compilationErrorList.Add(FormatDiagnostic(diagnostic));
            }
        }

        if (compilationErrorList.Count > 0)
        {
            return new LayerGraph(compilationErrorList.ToImmutableArray(), [], []);
        }

        var compilerRoot = Path.GetFullPath(RepositoryPaths.CompilerProject);
        var treeInfos = new Dictionary<SyntaxTree, TreeInfo>(compilation.SyntaxTrees.Length);
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (string.IsNullOrEmpty(tree.FilePath))
            {
                continue;
            }

            var relative = NormalizeRelativePath(compilerRoot, tree.FilePath);
            treeInfos[tree] = new TreeInfo(relative, ClassifyLayer(relative));
        }

        var forbiddenTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var forbiddenBySimpleName = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        foreach (var (tree, info) in treeInfos)
        {
            if (info.Layer != LayerKind.Forbidden)
            {
                continue;
            }

            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var node in root.DescendantNodes(descendIntoTrivia: false))
            {
                switch (node)
                {
                    case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax:
                        if (model.GetDeclaredSymbol(node) is INamedTypeSymbol typeSymbol)
                        {
                            var def = typeSymbol.OriginalDefinition;
                            forbiddenTypes.Add(def);
                            if (IsAccessibleFromOtherFiles(def))
                            {
                                AddSimpleNameTarget(forbiddenBySimpleName, def.Name, def);
                            }
                        }

                        break;
                    case MethodDeclarationSyntax methodSyntax:
                        if (model.GetDeclaredSymbol(methodSyntax) is IMethodSymbol { IsExtensionMethod: true } method &&
                            IsAccessibleFromOtherFiles(method))
                        {
                            AddSimpleNameTarget(forbiddenBySimpleName, method.Name, method.ContainingType.OriginalDefinition);
                        }

                        break;
                }
            }
        }

        var frozenForbiddenBySimpleName = forbiddenBySimpleName.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToImmutableArray(),
            StringComparer.Ordinal);

        var typeReferences = new Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        var walkedTrees = new HashSet<SyntaxTree>();
        var tier1Types = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        void WalkTree(SyntaxTree tree)
        {
            if (!walkedTrees.Add(tree))
            {
                return;
            }

            // Binding every body again for its diagnostics doubles the run time; the walker binds each name anyway and
            // reports the ones that do not bind, which is what a missing source or generated file shows up as
            var model = compilation.GetSemanticModel(tree);
            var walker = new ReferenceCollectorWalker(model, frozenForbiddenBySimpleName, typeReferences, compilationErrorList);
            walker.Visit(tree.GetRoot());
        }

        foreach (var (tree, info) in treeInfos)
        {
            if (info.Layer != LayerKind.Tier1)
            {
                continue;
            }

            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes(descendIntoTrivia: false))
            {
                if (node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax &&
                    model.GetDeclaredSymbol(node) is INamedTypeSymbol declared)
                {
                    tier1Types.Add(declared.OriginalDefinition);
                }
            }

            WalkTree(tree);
        }

        foreach (var tier1Type in tier1Types)
        {
            foreach (var syntaxRef in tier1Type.DeclaringSyntaxReferences)
            {
                if (treeInfos.TryGetValue(syntaxRef.SyntaxTree, out var info) &&
                    info.Layer is not (LayerKind.Forbidden or LayerKind.Tier1ExcludedStateMachine))
                {
                    WalkTree(syntaxRef.SyntaxTree);
                }
            }
        }

        var directEdges = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in tier1Types)
        {
            if (!typeReferences.TryGetValue(source, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                if (forbiddenTypes.Contains(target))
                {
                    directEdges.Add(FormatEdge(source, target, treeInfos));
                }
            }
        }

        var indirectEdges = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<INamedTypeSymbol>(tier1Types, SymbolEqualityComparer.Default);
        var queue = new Queue<INamedTypeSymbol>(tier1Types);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var syntaxRef in current.DeclaringSyntaxReferences)
            {
                if (treeInfos.TryGetValue(syntaxRef.SyntaxTree, out var info) &&
                    info.Layer is not (LayerKind.Forbidden or LayerKind.Tier1ExcludedStateMachine))
                {
                    WalkTree(syntaxRef.SyntaxTree);
                }
            }

            if (!typeReferences.TryGetValue(current, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                if (forbiddenTypes.Contains(target))
                {
                    if (!tier1Types.Contains(current))
                    {
                        indirectEdges.Add(FormatEdge(current, target, treeInfos));
                    }

                    continue;
                }

                if (IsExcludedStateMachineType(target, treeInfos))
                {
                    continue;
                }

                if (visited.Add(target))
                {
                    queue.Enqueue(target);
                }
            }
        }

        return new LayerGraph(
            CompilationErrors: compilationErrorList.ToImmutableArray(),
            DirectEdges: directEdges.Order(StringComparer.Ordinal).ToImmutableArray(),
            IndirectEdges: indirectEdges.Order(StringComparer.Ordinal).ToImmutableArray());
    }

    private static string FormatDiagnostic(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();
        var line = lineSpan.StartLinePosition.Line + 1;
        return $"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)} ({lineSpan.Path}:{line})";
    }

    private static bool IsExcludedStateMachineType(
        INamedTypeSymbol type,
        Dictionary<SyntaxTree, TreeInfo> treeInfos)
    {
        foreach (var syntaxRef in type.DeclaringSyntaxReferences)
        {
            if (treeInfos.TryGetValue(syntaxRef.SyntaxTree, out var info) &&
                info.Layer == LayerKind.Tier1ExcludedStateMachine)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddSimpleNameTarget(
        Dictionary<string, List<INamedTypeSymbol>> map,
        string name,
        INamedTypeSymbol target)
    {
        if (!map.TryGetValue(name, out var list))
        {
            list = [];
            map[name] = list;
        }

        list.Add(target);
    }

    private static bool IsAccessibleFromOtherFiles(ISymbol symbol)
    {
        for (var current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingType)
        {
            if (current is INamedTypeSymbol { IsFileLocal: true })
            {
                return false;
            }

            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    private static LayerKind ClassifyLayer(string relative)
    {
        if (relative.StartsWith("Transpiler/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("Lowering/", StringComparison.OrdinalIgnoreCase))
        {
            return LayerKind.Forbidden;
        }

        // The state machine of Ir/Linear is deleted with the syntax path; named one by one so that a new helper file
        // beside it is still checked
        if (relative is "Ir/Linear/IrStateMachine.cs" or "Ir/Linear/IrStateMachineBuilder.cs" or
            "Ir/Linear/IrStateMachineExit.cs")
        {
            return LayerKind.Tier1ExcludedStateMachine;
        }

        if (relative.StartsWith("Ir/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("Semantics/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("Dialects/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("Backends/", StringComparison.OrdinalIgnoreCase))
        {
            return LayerKind.Tier1;
        }

        return LayerKind.Other;
    }

    private static string NormalizeRelativePath(string compilerRoot, string filePath)
    {
        var relative = Path.GetRelativePath(compilerRoot, Path.GetFullPath(filePath));
        return string.Create(relative.Length, relative, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = source[i] == '\\' ? '/' : source[i];
            }
        });
    }

    private static string FormatEdge(
        INamedTypeSymbol source,
        INamedTypeSymbol target,
        Dictionary<SyntaxTree, TreeInfo> treeInfos)
    {
        var sourceFile = GetPreferredRelativePath(source, treeInfos);
        var targetFile = GetPreferredRelativePath(target, treeInfos);
        return $"{FormatTypeName(source)} ({sourceFile}) -> {FormatTypeName(target)} ({targetFile})";
    }

    private static string GetPreferredRelativePath(
        INamedTypeSymbol type,
        Dictionary<SyntaxTree, TreeInfo> treeInfos)
    {
        string? best = null;
        var bestIsObj = true;

        foreach (var syntaxRef in type.DeclaringSyntaxReferences)
        {
            if (!treeInfos.TryGetValue(syntaxRef.SyntaxTree, out var info))
            {
                continue;
            }

            var isObj = info.RelativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
            if (best is null ||
                (!isObj && bestIsObj) ||
                (isObj == bestIsObj && StringComparer.Ordinal.Compare(info.RelativePath, best) < 0))
            {
                best = info.RelativePath;
                bestIsObj = isObj;
            }
        }

        return best ?? string.Empty;
    }

    private static string FormatTypeName(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        var chain = new List<INamedTypeSymbol>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            chain.Add(current);
        }

        var builder = new StringBuilder();
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (i < chain.Count - 1)
            {
                builder.Append('.');
            }

            var part = chain[i];
            builder.Append(part.Name);
            if (part.TypeParameters.Length > 0)
            {
                builder.Append('<');
                for (var p = 0; p < part.TypeParameters.Length; p++)
                {
                    if (p > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(part.TypeParameters[p].Name);
                }

                builder.Append('>');
            }
        }

        return builder.ToString();
    }

    private enum LayerKind
    {
        Other,
        Tier1,
        Tier1ExcludedStateMachine,
        Forbidden
    }

    private readonly record struct TreeInfo(string RelativePath, LayerKind Layer);

    private sealed record LayerGraph(
        ImmutableArray<string> CompilationErrors,
        ImmutableArray<string> DirectEdges,
        ImmutableArray<string> IndirectEdges);

    private sealed class ReferenceCollectorWalker(
        SemanticModel model,
        Dictionary<string, ImmutableArray<INamedTypeSymbol>> forbiddenBySimpleName,
        Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>> typeReferences,
        List<string> unboundNames)
        : CSharpSyntaxWalker(SyntaxWalkerDepth.Node)
    {
        private readonly List<INamedTypeSymbol> _fileStaticOrAliasTargets = [];
        private readonly HashSet<ITypeSymbol> _visitedTypeGuard = new(SymbolEqualityComparer.Default);
        private INamedTypeSymbol? _currentType;

        public override void Visit(SyntaxNode? node)
        {
            if (node is null or DocumentationCommentTriviaSyntax or XmlCrefAttributeSyntax or CrefSyntax)
            {
                return;
            }

            if (node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            {
                var declared = (model.GetDeclaredSymbol(node) as INamedTypeSymbol)?.OriginalDefinition;
                if (declared is null)
                {
                    base.Visit(node);
                    return;
                }

                var previousType = _currentType;
                _currentType = declared;
                var refs = GetOrCreateReferenceSet(declared);
                if (previousType is null)
                {
                    foreach (var target in _fileStaticOrAliasTargets)
                    {
                        refs.Add(target);
                    }
                }

                base.Visit(node);
                _currentType = previousType;
                return;
            }

            base.Visit(node);
        }

        public override void VisitUsingDirective(UsingDirectiveSyntax node)
        {
            if (node.Alias is null && node.StaticKeyword.IsKind(SyntaxKind.None))
            {
                return;
            }

            if (node.Name is not null)
            {
                Visit(node.Name);
            }
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            InspectSimpleName(node);
            base.VisitIdentifierName(node);
        }

        public override void VisitGenericName(GenericNameSyntax node)
        {
            InspectSimpleName(node);
            base.VisitGenericName(node);
        }

        public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            InspectSymbolNode(node, includeImplicitTypeInfo: true);
            base.VisitImplicitObjectCreationExpression(node);
        }

        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            InspectSymbolNode(node, includeImplicitTypeInfo: false);
            base.VisitElementAccessExpression(node);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            InspectSymbolNode(node, includeImplicitTypeInfo: false);
            base.VisitBinaryExpression(node);
        }

        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            InspectSymbolNode(node, includeImplicitTypeInfo: false);
            base.VisitPrefixUnaryExpression(node);
        }

        public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            InspectSymbolNode(node, includeImplicitTypeInfo: false);
            base.VisitPostfixUnaryExpression(node);
        }

        public override void VisitCastExpression(CastExpressionSyntax node)
        {
            InspectSymbolNode(node, includeImplicitTypeInfo: false);
            base.VisitCastExpression(node);
        }

        private void InspectSimpleName(SimpleNameSyntax node)
        {
            var info = model.GetSymbolInfo(node);
            var resolved = ProcessSymbolInfo(info);

            if (node is IdentifierNameSyntax { IsVar: true } &&
                model.GetTypeInfo(node).Type is { } inferredType and not IErrorTypeSymbol)
            {
                VisitTypeSymbol(inferredType);
                resolved = true;
            }

            if (resolved || IsUnboundSyntacticName(node, model))
            {
                return;
            }

            var span = node.GetLocation().GetLineSpan();
            unboundNames.Add($"unbound name '{node.Identifier.ValueText}' ({span.Path}:{span.StartLinePosition.Line + 1})");
            if (forbiddenBySimpleName.TryGetValue(node.Identifier.ValueText, out var candidates))
            {
                foreach (var candidate in candidates)
                {
                    RecordTarget(candidate);
                }
            }
        }

        private void InspectSymbolNode(SyntaxNode node, bool includeImplicitTypeInfo)
        {
            var info = model.GetSymbolInfo(node);
            var resolved = ProcessSymbolInfo(info);

            if (!resolved &&
                includeImplicitTypeInfo &&
                model.GetTypeInfo(node).Type is { } implicitType and not IErrorTypeSymbol)
            {
                VisitTypeSymbol(implicitType);
            }
        }

        private bool ProcessSymbolInfo(SymbolInfo info)
        {
            if (info.Symbol is { } symbol and not IErrorTypeSymbol)
            {
                VisitSymbol(symbol);
                return true;
            }

            if (!info.CandidateSymbols.IsEmpty)
            {
                var anyValid = false;
                foreach (var candidate in info.CandidateSymbols)
                {
                    if (candidate is not IErrorTypeSymbol)
                    {
                        VisitSymbol(candidate);
                        anyValid = true;
                    }
                }

                return anyValid;
            }

            return false;
        }

        private void VisitSymbol(ISymbol symbol)
        {
            if (symbol is IAliasSymbol alias)
            {
                symbol = alias.Target;
            }

            switch (symbol)
            {
                case INamespaceSymbol or ITypeParameterSymbol or IDiscardSymbol or IRangeVariableSymbol:
                    return;
                case ILocalSymbol local:
                    VisitTypeSymbol(local.Type);
                    return;
                case IParameterSymbol parameter:
                    VisitTypeSymbol(parameter.Type);
                    return;
                case IMethodSymbol method:
                    // The value a call hands over counts as a use of its type even when no name spells it
                    VisitTypeSymbol(method.ReturnType);
                    foreach (var parameter in method.Parameters)
                    {
                        VisitTypeSymbol(parameter.Type);
                    }

                    method = method.ReducedFrom ?? method;
                    if (method.ContainingType is not null)
                    {
                        VisitTypeSymbol(method.ContainingType);
                    }

                    foreach (var typeArg in method.TypeArguments)
                    {
                        VisitTypeSymbol(typeArg);
                    }

                    return;
                case IPropertySymbol property:
                    VisitTypeSymbol(property.Type);
                    VisitContainingType(property);
                    return;
                case IFieldSymbol field:
                    VisitTypeSymbol(field.Type);
                    VisitContainingType(field);
                    return;
                case IEventSymbol eventSymbol:
                    VisitTypeSymbol(eventSymbol.Type);
                    VisitContainingType(eventSymbol);
                    return;
                case ITypeSymbol typeSymbol:
                    VisitTypeSymbol(typeSymbol);
                    return;
            }
        }

        private void VisitContainingType(ISymbol member)
        {
            if (member.ContainingType is not null)
            {
                VisitTypeSymbol(member.ContainingType);
            }
        }

        private void VisitTypeSymbol(ITypeSymbol type)
        {
            _visitedTypeGuard.Clear();
            VisitTypeSymbolCore(type);
        }

        private void VisitTypeSymbolCore(ITypeSymbol type)
        {
            if (!_visitedTypeGuard.Add(type))
            {
                return;
            }

            switch (type)
            {
                case IArrayTypeSymbol arrayType:
                    VisitTypeSymbolCore(arrayType.ElementType);
                    return;
                case IPointerTypeSymbol pointerType:
                    VisitTypeSymbolCore(pointerType.PointedAtType);
                    return;
                case IFunctionPointerTypeSymbol functionPointer:
                    VisitTypeSymbolCore(functionPointer.Signature.ReturnType);
                    foreach (var param in functionPointer.Signature.Parameters)
                    {
                        VisitTypeSymbolCore(param.Type);
                    }

                    return;
                case INamedTypeSymbol namedType:
                    if (namedType.IsTupleType)
                    {
                        foreach (var element in namedType.TupleElements)
                        {
                            VisitTypeSymbolCore(element.Type);
                        }

                        return;
                    }

                    var def = namedType.OriginalDefinition;
                    if (!def.DeclaringSyntaxReferences.IsEmpty)
                    {
                        RecordTarget(def);
                    }

                    for (var current = namedType; current is not null; current = current.ContainingType)
                    {
                        foreach (var typeArg in current.TypeArguments)
                        {
                            VisitTypeSymbolCore(typeArg);
                        }
                    }

                    return;
            }
        }

        private void RecordTarget(INamedTypeSymbol target)
        {
            if (_currentType is null)
            {
                _fileStaticOrAliasTargets.Add(target);
                return;
            }

            GetOrCreateReferenceSet(_currentType).Add(target);
        }

        private HashSet<INamedTypeSymbol> GetOrCreateReferenceSet(INamedTypeSymbol owner)
        {
            if (!typeReferences.TryGetValue(owner, out var set))
            {
                set = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                typeReferences[owner] = set;
            }

            return set;
        }

        private static bool IsUnboundSyntacticName(SimpleNameSyntax node, SemanticModel model)
        {
            if (node.Parent is NameColonSyntax or NameEqualsSyntax)
            {
                return true;
            }

            // var, and the contextual keywords of a type constraint, which bind to no type
            if (node is IdentifierNameSyntax { IsVar: true } or IdentifierNameSyntax { IsNotNull: true } or
                IdentifierNameSyntax { IsUnmanaged: true })
            {
                return true;
            }

            if (node.Parent is InvocationExpressionSyntax invocation &&
                invocation.Expression == node &&
                string.Equals(node.Identifier.ValueText, "nameof", StringComparison.Ordinal) &&
                model.GetConstantValue(invocation).HasValue)
            {
                return true;
            }

            return false;
        }
    }
}
