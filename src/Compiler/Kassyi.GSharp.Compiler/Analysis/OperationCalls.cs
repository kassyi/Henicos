using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The methods that one operation calls by itself, whatever the syntax that wrote the call: an invocation, a
/// constructor (<c>new T(...)</c>, target-typed <c>new()</c>, <c>new T()</c> on a type parameter), the getter or setter
/// of a property or indexer read or written (also through a bare name or <c>using static</c>), and a user-defined
/// operator or conversion (unary, binary, compound assignment, increment, conversion).
/// </summary>
internal static class OperationCalls
{
    /// <summary>
    /// Adds to <paramref name="methods"/> each method that <paramref name="operation"/> itself calls (not those of its
    /// children). <paramref name="typeArguments"/> replaces the type parameters of the generic declaration being walked
    /// by the type arguments of the construction being analyzed, so that <c>new T()</c> and calls that pass <c>T</c> on
    /// reach the constructed members.
    /// </summary>
    public static void Add(IOperation operation, TypeArgumentMap typeArguments,
        ImmutableArray<IMethodSymbol>.Builder methods)
    {
        switch (operation)
        {
            case ITypeParameterObjectCreationOperation { Type: { } created }:
                if (typeArguments.Substitute(created) is INamedTypeSymbol { TypeKind: TypeKind.Struct } type &&
                    FindParameterlessConstructor(type) is { } parameterless)
                {
                    methods.Add(parameterless);
                }

                break;
            case IPropertyReferenceOperation property:
                AddAccessors(property, typeArguments, methods);
                break;
            case ICompoundAssignmentOperation compound:
                AddNonNull(compound.OperatorMethod, typeArguments, methods);
                AddNonNull(compound.InConversion.MethodSymbol, typeArguments, methods);
                AddNonNull(compound.OutConversion.MethodSymbol, typeArguments, methods);
                break;
            default:
                AddNonNull(GetCalledMethod(operation), typeArguments, methods);
                break;
        }
    }

    // The one method that an invocation, a constructor call or a user-defined operator or conversion calls
    private static IMethodSymbol? GetCalledMethod(IOperation operation) => operation switch
    {
        IInvocationOperation invocation => invocation.TargetMethod,
        IObjectCreationOperation creation => creation.Constructor,
        IUnaryOperation unary => unary.OperatorMethod,
        IBinaryOperation binary => binary.OperatorMethod,
        IIncrementOrDecrementOperation increment => increment.OperatorMethod,
        IConversionOperation conversion => conversion.OperatorMethod,
        _ => null
    };

    // The getter of a property that is read, the setter of one that is assigned, both for a compound assignment,
    // increment or ??=
    private static void AddAccessors(IPropertyReferenceOperation reference, TypeArgumentMap typeArguments,
        ImmutableArray<IMethodSymbol>.Builder methods)
    {
        var property = reference.Property;
        var (reads, writes) = reference.Parent switch
        {
            ISimpleAssignmentOperation assignment when assignment.Target == reference => (false, true),
            ICompoundAssignmentOperation compound when compound.Target == reference => (true, true),
            ICoalesceAssignmentOperation coalesce when coalesce.Target == reference => (true, true),
            IIncrementOrDecrementOperation => (true, true),
            ITupleOperation { Parent: IDeconstructionAssignmentOperation } => (false, true),
            _ => (true, false)
        };

        if (reads)
        {
            AddNonNull(property.GetMethod, typeArguments, methods);
        }

        if (writes)
        {
            AddNonNull(property.SetMethod, typeArguments, methods);
        }
    }

    private static void AddNonNull(IMethodSymbol? method, TypeArgumentMap typeArguments,
        ImmutableArray<IMethodSymbol>.Builder methods)
    {
        if (method != null)
        {
            methods.Add(typeArguments.Substitute(method));
        }
    }

    /// <summary>
    /// The parameterless constructor of <paramref name="type"/> that <c>new T()</c> runs: a declared one, or the
    /// implicit one (which runs nothing).
    /// </summary>
    public static IMethodSymbol? FindParameterlessConstructor(INamedTypeSymbol type) =>
        type.InstanceConstructors
            .AsStackEnumerable()
            .FirstOrDefault(static c => c.Parameters.Length == 0);
}
