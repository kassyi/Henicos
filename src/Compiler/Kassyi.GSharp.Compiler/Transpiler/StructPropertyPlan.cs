using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>What an accessor member function of a struct property runs.</summary>
internal enum PropertyAccessorBody : byte
{
    /// <summary>No member function: the property has no such accessor, or its body is not in this compilation.</summary>
    None,

    /// <summary>The body written in C# (<c>get =&gt; ...</c>, <c>set { ... }</c>, or the property's <c>=&gt; ...</c>).</summary>
    Written,

    /// <summary>The body the C# compiler implements for <c>get;</c>: <c>return &lt;backing field&gt;;</c>.</summary>
    ReadsBackingField,

    /// <summary>The body the C# compiler implements for <c>set;</c> / <c>init;</c>: <c>&lt;backing field&gt; = value;</c>.</summary>
    WritesBackingField
}

/// <summary>One accessor of a struct property: what it runs, and the written body when it has one.</summary>
/// <param name="Body">What the accessor runs.</param>
/// <param name="Block">The block of a <see cref="PropertyAccessorBody.Written"/> body, if it is a block.</param>
/// <param name="Expression">The expression of a <see cref="PropertyAccessorBody.Written"/> body, if it is an expression.</param>
/// <param name="SyntaxTree">The tree of the written body, bound with a model of its own.</param>
internal readonly record struct PropertyAccessorPlan(
    PropertyAccessorBody Body,
    BlockSyntax? Block,
    ExpressionSyntax? Expression,
    SyntaxTree? SyntaxTree)
{
    public static readonly PropertyAccessorPlan None = new(PropertyAccessorBody.None, null, null, null);

    public static PropertyAccessorPlan Implemented(PropertyAccessorBody body) => new(body, null, null, null);

    public static PropertyAccessorPlan Written(BlockSyntax? block, ExpressionSyntax? expression, SyntaxTree tree) =>
        new(PropertyAccessorBody.Written, block, expression, tree);
}

/// <summary>
/// The accessor member functions of a struct property whose accessors G# emits
/// (<see cref="StructAbiMembers.HasAccessorFunctions"/>), decided before anything is written
/// (<see cref="StructPropertyEmitter"/> writes them).
/// </summary>
/// <param name="Getter">The getter.</param>
/// <param name="Setter">The setter or <c>init</c> accessor.</param>
/// <param name="ValueParameter">The <c>value</c> parameter of the setter, when it has one.</param>
/// <param name="IsConstGetter">Whether the getter is a <c>const</c> member function (<c>ComputedPropertyGetterCall.IsConstGetter</c>).</param>
internal readonly record struct StructPropertyPlan(
    PropertyAccessorPlan Getter,
    PropertyAccessorPlan Setter,
    IParameterSymbol? ValueParameter,
    bool IsConstGetter);

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
        return new StructPropertyPlan(
            getter,
            setter,
            setter.Body != PropertyAccessorBody.None && property.SetMethod is { Parameters: [var value] } ? value : null,
            ComputedPropertyGetterCall.IsConstGetter(property, compilation)
        );
    }

    private static PropertyAccessorPlan PlanAccessor(IPropertySymbol property, IMethodSymbol? accessor,
        PropertyAccessorBody implementedBody, Compilation compilation)
    {
        if (accessor is null ||
            FindDeclaration(property, accessor) is not { } declaration ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        {
            return PropertyAccessorPlan.None;
        }

        return declaration switch
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
    }

    // The declaration of the accessor that has the body: the implementing part of a partial property
    private static SyntaxNode? FindDeclaration(IPropertySymbol property, IMethodSymbol accessor)
    {
        var implementation = accessor.PartialImplementationPart ?? accessor;
        var declaration = implementation.DeclaringSyntaxReferences.Length > 0
            ? implementation.DeclaringSyntaxReferences[0].GetSyntax()
            : null;
        if (declaration is not AccessorDeclarationSyntax { Parent.Parent: PropertyDeclarationSyntax propertyDeclaration } ||
            !propertyDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return declaration;
        }

        // [WHY] Roslyn 4.8, which the compiler builds against, links the parts of a partial method but not of a partial
        // property, so the implementing part is found by name among the declarations of the type
        return FindImplementingAccessor(property, declaration.Kind()) ?? declaration;
    }

    private static AccessorDeclarationSyntax? FindImplementingAccessor(IPropertySymbol property, SyntaxKind accessorKind)
    {
        foreach (var typeReference in property.ContainingType.DeclaringSyntaxReferences)
        {
            if (typeReference.GetSyntax() is not TypeDeclarationSyntax type)
            {
                continue;
            }

            foreach (var member in type.Members)
            {
                if (member is PropertyDeclarationSyntax { AccessorList: { } accessors } candidate &&
                    candidate.Identifier.ValueText == property.Name &&
                    candidate.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                    accessors.Accessors
                        .AsStackEnumerable()
                        .Any(static a => a.Body != null || a.ExpressionBody != null))
                {
                    return accessors.Accessors
                        .AsStackEnumerable()
                        .FirstOrDefault(accessorKind, static (a, kind) => a.IsKind(kind));
                }
            }
        }

        return null;
    }
}
