namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The syntax that runs when a function is called, for <see cref="DeviceFunctionEffects"/>.
/// </summary>
internal static class DeviceFunctionBodies
{
    // The syntax that runs when the function is called: the body of a method or accessor, or the body and constructor
    // initializer of a constructor together with the instance member initializers of its struct
    // Only syntax of the compilation counts: a function of another assembly has none here, also when an IDE gives its
    // declaration through a referenced compilation
    internal static bool TryGet(IMethodSymbol method, Compilation compilation, out ImmutableArray<SyntaxNode> bodies)
    {
        if (IsDefaultStructConstructor(method))
        {
            bodies = default;
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<SyntaxNode>();
        if (method.MethodKind == MethodKind.Constructor && method.ContainingType is { TypeKind: TypeKind.Struct } type &&
            !DeviceExportTable.IsOutside(type, compilation))
        {
            AddStructInstanceInitializers(type, compilation, builder);

            if (method.TryGetDeclaringSyntax<ConstructorDeclarationSyntax>(out var constructor))
            {
                AddNonNull(builder, constructor.Initializer);
                AddNonNull(builder, (SyntaxNode?)constructor.Body ?? constructor.ExpressionBody);
            }

            bodies = builder.ToImmutable();
            return true;
        }

        if (method.TryGetDeclaringSyntax<SyntaxNode>(out var syntax) && compilation.ContainsSyntaxTree(syntax.SyntaxTree) &&
            IsFunctionWithBody(method, syntax))
        {
            AddNonNull(builder, syntax switch
            {
                LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody,
                BaseMethodDeclarationSyntax declaration => (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody,
                AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody,
                ArrowExpressionClauseSyntax arrow => arrow,
                _ => null
            });
            bodies = builder.ToImmutable();
            return bodies.Length > 0;
        }

        bodies = default;
        return false;
    }

    // A [GpuDeviceFunction], a member of a struct that G# defines on the device, or a local function
    private static bool IsFunctionWithBody(IMethodSymbol method, SyntaxNode syntax) =>
        (syntax is LocalFunctionStatementSyntax && method.MethodKind == MethodKind.LocalFunction) ||
        (syntax is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or ArrowExpressionClauseSyntax &&
         (HasDeviceFunctionAttribute(method) ||
          (method.ContainingType is { } type && EmittedUserTypes.IsEmittedUserType(type))));

    private static bool HasDeviceFunctionAttribute(IMethodSymbol method)
    {
        var original = (method.ReducedFrom ?? method).OriginalDefinition;
        return original.HasAttribute(KnownAttributes.GpuDeviceFunction) ||
               (original.PartialImplementationPart is { } implementation &&
                implementation.HasAttribute(KnownAttributes.GpuDeviceFunction)) ||
               (original.PartialDefinitionPart is { } definition &&
                definition.HasAttribute(KnownAttributes.GpuDeviceFunction));
    }

    internal static void AddStructInstanceInitializers(INamedTypeSymbol type, Compilation compilation,
        ImmutableArray<SyntaxNode>.Builder builder)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax declaration &&
                compilation.ContainsSyntaxTree(declaration.SyntaxTree))
            {
                AddInstanceInitializers(declaration, builder);
            }
        }
    }

    private static void AddInstanceInitializers(TypeDeclarationSyntax declaration,
        ImmutableArray<SyntaxNode>.Builder bodies)
    {
        foreach (var member in declaration.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field when !field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                                                       !field.Modifiers.Any(SyntaxKind.ConstKeyword):
                    foreach (var variable in field.Declaration.Variables)
                    {
                        AddNonNull(bodies, variable.Initializer);
                    }

                    break;
                case PropertyDeclarationSyntax property when !property.Modifiers.Any(SyntaxKind.StaticKeyword):
                    AddNonNull(bodies, property.Initializer);
                    break;
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="method"/> is the implicit parameterless constructor of a value type, which
    /// zero-initializes (<c>default(T)</c>) and runs no field/property initializers or body.
    /// </summary>
    internal static bool IsDefaultStructConstructor(IMethodSymbol method) =>
        method is { IsImplicitlyDeclared: true, Parameters.Length: 0, MethodKind: MethodKind.Constructor } &&
        method.ContainingType.IsValueType;

    private static void AddNonNull(ImmutableArray<SyntaxNode>.Builder builder, SyntaxNode? node)
    {
        if (node != null)
        {
            builder.Add(node);
        }
    }
}
