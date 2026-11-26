#nullable enable
namespace Kassyi.GSharp.Tests.Cpu.Transpiler;

/// <summary>
/// Collects, per declared type, the types its syntax references by symbol, for <see cref="CompilerLayerGraph"/>.
/// A name that does not bind is reported, and counts as each forbidden type or extension method of that name.
/// </summary>
internal sealed class CompilerLayerReferenceWalker(
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
