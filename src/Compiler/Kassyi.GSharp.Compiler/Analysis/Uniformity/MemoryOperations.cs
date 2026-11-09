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
    // is part of that location, so it is written with it (#1272)
    public static IOperation ContainingLocation(IOperation target)
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
