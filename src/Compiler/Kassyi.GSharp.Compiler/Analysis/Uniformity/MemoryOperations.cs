using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The shapes of operation that <see cref="MemoryUniformity"/> follows: which operations are locations of memory the
/// threads share, and through which variable a reference reaches it.
/// </summary>
internal static class MemoryOperations
{
    // Whether target is a location that may be memory: an element, a ref local, a ref or out parameter, the result of
    // a call that returns by reference (a ref conditional reaches here as a flow capture, which Bind takes as any
    // memory)
    public static bool IsLocation(IOperation target) => target switch
    {
        IArrayElementReferenceOperation => true,
        IPropertyReferenceOperation { Property.IsIndexer: true, Instance: not null } => true,
        ILocalReferenceOperation { Local.IsRef: true } => true,
        IParameterReferenceOperation { Parameter: { RefKind: RefKind.Ref or RefKind.Out } parameter } =>
            !IsMemoryType(parameter.Type),
        IInvocationOperation { TargetMethod.ReturnsByRef: true } => true,
        _ => false
    };

    // The variable through which reference reaches memory the threads share, or null; a pointer is not followed
    public static ISymbol? GetName(IOperation reference)
    {
        ISymbol? symbol = Unwrap(reference) switch
        {
            IParameterReferenceOperation parameter => parameter.Parameter,
            ILocalReferenceOperation { Local: { IsRef: false } local } => local,
            IFieldReferenceOperation field => field.Field,
            IPropertyReferenceOperation { Property: { IsIndexer: false } property } => property,
            _ => null
        };

        return symbol != null && IsMemoryType(GetMemoryType(symbol)) ? symbol : null;
    }

    // The location that writing target changes: a field of a struct location (a[i].X, a[i].Inner.X, r.X of a ref local)
    // is part of that location, so it is written with it (#1272). The assigned member itself may also be a property or
    // an indexer of the struct (a[i].P = v, a[i][0] = v), whose setter runs on the location; a property further in
    // cannot be assigned through (C# reads it into a copy)
    public static IOperation ContainingLocation(IOperation target)
    {
        target = Unwrap(target);
        if (GetAssignedPropertyInstance(target) is { } owner)
        {
            target = Unwrap(owner);
        }

        return ContainingFieldLocation(target);
    }

    // The location whose fields hold target: a[i].Inner.X is a[i]. A call on a[i].P.Bump() runs on the copy the getter
    // of P returns, so a property ends the walk (it is not part of the location)
    public static IOperation ContainingFieldLocation(IOperation target)
    {
        target = Unwrap(target);
        while (target is IFieldReferenceOperation
               {
                   Field.IsStatic: false,
                   Instance: { Type.IsValueType: true } instance
               })
        {
            target = Unwrap(instance);
        }

        return target;
    }

    // The struct whose member an assignment writes when the member is an auto-property (its backing field) or a
    // property or indexer with a setter that is not readonly: the setter runs on the instance and may write any field
    // of it, so the instance is taken as written (the safe side; a setter that writes nothing of this counts too). A
    // span or array indexer has no setter and stays a location of its own
    private static IOperation? GetAssignedPropertyInstance(IOperation target) => target switch
    {
        IPropertyReferenceOperation
        {
            Property: { IsStatic: false } property,
            Instance: { Type.IsValueType: true } instance
        } when (!property.IsIndexer && IsAutoProperty(property)) || property.SetMethod is { IsReadOnly: false } =>
            instance,
        _ => null
    };

    private static bool IsAutoProperty(IPropertySymbol property) =>
        property.ContainingType.GetMembers()
            .AsStackEnumerable()
            .Any(property, static (member, p) => member is IFieldSymbol field && field.AssociatedSymbol.IsEqualTo(p));

    public static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                case IDeclarationExpressionOperation declaration:
                    operation = declaration.Expression;
                    continue;
                default:
                    return operation;
            }
        }
    }

    public static ITypeSymbol? GetMemoryType(ISymbol symbol) => symbol switch
    {
        IParameterSymbol parameter => parameter.Type,
        ILocalSymbol local => local.Type,
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        _ => null
    };

    public static bool IsMemoryType(ITypeSymbol? type) =>
        type is IArrayTypeSymbol ||
        type.IsSharedMemoryType() ||
        (type is INamedTypeSymbol { Name: TypeConstants.SpanType or TypeConstants.ReadOnlySpanType } span &&
         span.ContainingNamespace.IsSystemNamespace());
}
