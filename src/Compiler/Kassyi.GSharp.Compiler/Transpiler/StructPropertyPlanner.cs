using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Decides the <see cref="StructPropertyPlan"/> of a property.</summary>
internal static class StructPropertyPlanner
{
    /// <summary>The accessor member functions of <paramref name="property"/>.</summary>
    /// <remarks>
    /// An accessor whose declaration is in another compilation (a struct from metadata) gets none: the struct's record
    /// brings it (<c>ImportedDeviceCodeRule</c>). An accessor without a body has the body that the C# compiler
    /// implements, which reads or writes the backing field; only a property with a backing field has one.
    /// </remarks>
    public static StructPropertyPlan Plan(IPropertySymbol property, Compilation compilation)
    {
        var getter = PlanAccessor(property, property.GetMethod, PropertyAccessorBody.ReadsBackingField, compilation);
        var setter = PlanAccessor(property, property.SetMethod, PropertyAccessorBody.WritesBackingField, compilation);
        var valueParameter = setter.Body != PropertyAccessorBody.None &&
                             property.SetMethod is { Parameters: [var value] }
            ? value
            : null;
        return new StructPropertyPlan(
            getter,
            setter,
            valueParameter,
            PropertyAccessorCall.IsConstGetter(property, compilation)
        );
    }

    private static PropertyAccessorPlan PlanAccessor(IPropertySymbol property, IMethodSymbol? accessor,
        PropertyAccessorBody implementedBody, Compilation compilation) =>
        accessor is not null &&
        FindDeclaration(property, accessor) is { } declaration &&
        compilation.ContainsSyntaxTree(declaration.SyntaxTree)
            ? ToAccessorPlan(declaration, implementedBody)
            : PropertyAccessorPlan.None;

    // What the declaration of an accessor runs: the written body, or the one C# implements for an accessor without one
    private static PropertyAccessorPlan ToAccessorPlan(SyntaxNode declaration, PropertyAccessorBody implementedBody) =>
        declaration switch
        {
            // The getter of int P => <expression>;
            ArrowExpressionClauseSyntax arrow => PropertyAccessorPlan.Written(null, arrow.Expression, arrow.SyntaxTree),
            AccessorDeclarationSyntax { Body: null, ExpressionBody: null } =>
                PropertyAccessorPlan.Implemented(implementedBody),
            AccessorDeclarationSyntax written => PropertyAccessorPlan.Written(
                written.Body,
                written.ExpressionBody?.Expression,
                written.SyntaxTree
            ),
            _ => PropertyAccessorPlan.None
        };

    // The declaration of the accessor that has the body: the implementing part of a partial property
    private static SyntaxNode? FindDeclaration(IPropertySymbol property, IMethodSymbol accessor)
    {
        var implementation = accessor.PartialImplementationPart ?? accessor;
        var declaration = implementation.DeclaringSyntaxReferences.Length > 0
            ? implementation.DeclaringSyntaxReferences[0].GetSyntax()
            : null;
        if (declaration is not AccessorDeclarationSyntax
            {
                Parent.Parent: PropertyDeclarationSyntax propertyDeclaration
            } ||
            !propertyDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return declaration;
        }

        // [WHY] Roslyn 4.8, which the compiler builds against, links the parts of a partial method but not of a
        // partial property, so the implementing part is found by name among the declarations of the type
        return FindImplementingAccessor(property, declaration.Kind()) ?? declaration;
    }

    private static AccessorDeclarationSyntax? FindImplementingAccessor(IPropertySymbol property,
        SyntaxKind accessorKind)
    {
        foreach (var typeReference in property.ContainingType.DeclaringSyntaxReferences)
        {
            if (typeReference.GetSyntax() is TypeDeclarationSyntax type &&
                type.Members
                    .AsStackEnumerable()
                    .OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault(property.Name, static (candidate, name) => IsImplementingPart(candidate, name))
                    is { AccessorList: { } accessors })
            {
                return accessors.Accessors
                    .AsStackEnumerable()
                    .FirstOrDefault(accessorKind, static (a, kind) => a.IsKind(kind));
            }
        }

        return null;
    }

    // The part of the partial property <name> that has the accessor bodies
    private static bool IsImplementingPart(PropertyDeclarationSyntax candidate, string name) =>
        candidate.Identifier.ValueText == name &&
        candidate.Modifiers.Any(SyntaxKind.PartialKeyword) &&
        candidate.AccessorList is { } accessors &&
        accessors.Accessors
            .AsStackEnumerable()
            .Any(static a => a.Body != null || a.ExpressionBody != null);
}
