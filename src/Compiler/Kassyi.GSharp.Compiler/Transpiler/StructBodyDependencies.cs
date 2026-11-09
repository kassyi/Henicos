namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The types named in the bodies that the device definition of a struct emits: the constructors written as factory
/// functions with the instance member initializers they run, the accessors of the computed properties, and the
/// conversion and truth operators emitted after the struct.
/// </summary>
/// <remarks>
/// [WHY] A struct whose constructor creates another struct needs that struct's definition and constructor function in
/// front of its own, although no field and no kernel names it (#1158). The bodies are walked once per struct and
/// compilation; the types of a generic struct are given with its type arguments substituted.
/// </remarks>
internal static class StructBodyDependencies
{
    private static readonly ConditionalWeakTable<Compilation,
        ConcurrentDictionary<INamedTypeSymbol, ImmutableArray<ITypeSymbol>>> s_types = new();

    /// <summary>
    /// The types named in the emitted bodies of <paramref name="structSym"/> that are, or contain, a user struct (an
    /// array-like of one included); the struct itself is not listed.
    /// </summary>
    public static ImmutableArray<ITypeSymbol> Get(INamedTypeSymbol structSym, Compilation compilation)
    {
        var cache = s_types.GetValue(compilation, static _ =>
            new ConcurrentDictionary<INamedTypeSymbol, ImmutableArray<ITypeSymbol>>(SymbolEqualityComparer.Default));
        return cache.TryGetValue(structSym, out var cached)
            ? cached
            : cache.GetOrAdd(structSym, Collect(structSym, compilation));
    }

    private static ImmutableArray<ITypeSymbol> Collect(INamedTypeSymbol structSym, Compilation compilation)
    {
        var definition = structSym.OriginalDefinition;
        var types = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var reference in definition.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not TypeDeclarationSyntax declaration ||
                !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
            {
                continue;
            }

            var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
            foreach (var member in declaration.Members)
            {
                AddMemberTypes(member, model, compilation, types);
            }
        }

        var result = ImmutableArray.CreateBuilder<ITypeSymbol>(types.Count);
        foreach (var type in types)
        {
            if (StructTypeSubstitution.SubstituteTypeParameters(type, structSym, compilation) is { } substituted &&
                !type.IsEqualTo(definition) && !substituted.IsEqualTo(structSym))
            {
                result.Add(substituted);
            }
        }

        return result.ToImmutable();
    }

    // The types of the syntax of one member that the device definition emits as code
    private static void AddMemberTypes(MemberDeclarationSyntax member, SemanticModel model, Compilation compilation,
        HashSet<ITypeSymbol> types)
    {
        switch (member)
        {
            case FieldDeclarationSyntax field when !field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                                                   !field.Modifiers.Any(SyntaxKind.ConstKeyword):
                foreach (var variable in field.Declaration.Variables)
                {
                    AddTypes(variable.Initializer, model, types);
                }

                break;
            case PropertyDeclarationSyntax property when !property.Modifiers.Any(SyntaxKind.StaticKeyword):
                AddTypes(property.Initializer, model, types);
                if (model.GetDeclaredSymbol(property) is { } propertySymbol &&
                    StructAbiMembers.IsComputedProperty(propertySymbol))
                {
                    AddTypes(property.ExpressionBody, model, types);
                    AddTypes(property.AccessorList, model, types);
                }

                break;
            case ConstructorDeclarationSyntax constructor
                when model.GetDeclaredSymbol(constructor) is { } constructorSymbol &&
                     StructConstructorEmitter.IsEmittedStructConstructor(constructorSymbol, compilation):
                AddTypes(constructor.Initializer, model, types);
                AddTypes(constructor.Body, model, types);
                AddTypes(constructor.ExpressionBody, model, types);
                break;
            case ConversionOperatorDeclarationSyntax conversion
                when model.GetDeclaredSymbol(conversion) is { } conversionSymbol &&
                     (StructConversionEmitter.IsEmittedStructConversion(conversionSymbol, compilation) ||
                      StructConversionEmitter.HasRefStructConversionBody(conversionSymbol, compilation)):
                AddTypes(conversion.Body, model, types);
                AddTypes(conversion.ExpressionBody, model, types);
                break;
            case OperatorDeclarationSyntax truthOperator
                when model.GetDeclaredSymbol(truthOperator) is { } operatorSymbol &&
                     StructTruthOperatorEmitter.IsEmittedTruthOperator(operatorSymbol, compilation):
                AddTypes(truthOperator.Body, model, types);
                AddTypes(truthOperator.ExpressionBody, model, types);
                break;
        }
    }

    // The type and converted type of every expression under body (a type name, a creation, a default literal)
    private static void AddTypes(SyntaxNode? body, SemanticModel model, HashSet<ITypeSymbol> types)
    {
        if (body == null)
        {
            return;
        }

        foreach (var node in body.DescendantNodesAndSelf())
        {
            if (node is ExpressionSyntax expression)
            {
                var info = model.GetTypeInfo(expression);
                AddIfStructLike(info.Type, types);
                AddIfStructLike(info.ConvertedType, types);
            }
        }
    }

    // A type that is, or after the substitution of a type parameter can be, a user struct or an array-like of one
    private static void AddIfStructLike(ITypeSymbol? type, HashSet<ITypeSymbol> types)
    {
        var element = type.TryGetArrayLikeElementType(out var elementType) ? elementType : type;
        if (type != null &&
            (element is ITypeParameterSymbol ||
             (element is INamedTypeSymbol named && GlobalCppNames.IsEmittedUserType(named))))
        {
            types.Add(type);
        }
    }
}
