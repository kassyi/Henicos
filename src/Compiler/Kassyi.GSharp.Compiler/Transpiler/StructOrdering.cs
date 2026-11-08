namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Collects the user structs that device code uses and orders them so that each struct is defined after the structs
/// it depends on.
/// </summary>
internal static class StructOrdering
{
    /// <summary>
    /// Recursively registers referenced struct symbols and their field types to ensure necessary forward definitions
    /// are emitted.
    /// </summary>
    public static void RegisterUsedStructIfApplicable(ITypeSymbol? typeSymbol, ref TranspileContext context)
    {
        typeSymbol = StructTypeSubstitution.SubstituteTypeParameters(typeSymbol, context.Emitter.CurrentStructType,
            context.Query.SemanticModel.Compilation);
        if (!TryGetUserStruct(typeSymbol, out var namedStruct))
        {
            return;
        }

        if (!context.Query.UsedStructs.Add(namedStruct))
        {
            return;
        }

        foreach (var typeArgument in namedStruct.TypeArguments)
        {
            RegisterUsedStructIfApplicable(typeArgument, ref context);
        }

        foreach (var member in StructAbiMembers.Get(namedStruct))
        {
            RegisterUsedStructIfApplicable(member.Type, ref context);
        }

        RegisterConversionTypes(namedStruct, ref context);
    }

    // The other type of a conversion that is emitted from its body (a ref struct's included) is named there
    private static void RegisterConversionTypes(INamedTypeSymbol namedStruct, ref TranspileContext context)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        foreach (var member in namedStruct.GetMembers())
        {
            if (member is not IMethodSymbol conversion ||
                !(StructConversionEmitter.IsEmittedStructConversion(conversion, compilation) ||
                  StructConversionEmitter.HasRefStructConversionBody(conversion, compilation)))
            {
                continue;
            }

            RegisterUsedStructIfApplicable(conversion.ReturnType, ref context);
            foreach (var parameter in conversion.Parameters)
            {
                RegisterUsedStructIfApplicable(parameter.Type, ref context);
            }
        }
    }

    /// <summary>
    /// Topologically sorts referenced structs in the current context so that dependent nested structs are declared
    /// first in C++.
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> GetTopologicallySortedStructs(ref TranspileContext context)
        => GetTopologicallySortedStructs(context.Query.UsedStructs);

    /// <summary>
    /// Topologically sorts referenced structs so that dependent nested structs are declared first in C++.
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> GetTopologicallySortedStructs(
        IEnumerable<INamedTypeSymbol> usedStructs)
    {
        // [WHY] CUDA C++ requires full struct definitions in dependency order before value-type usage as fields.
        var result = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var structSet = usedStructs as ISet<INamedTypeSymbol> ??
                        new HashSet<INamedTypeSymbol>(usedStructs, SymbolEqualityComparer.Default);
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var visiting = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var s in structSet)
        {
            visit(s);
        }

        return result.ToImmutable();

        void visit(INamedTypeSymbol s)
        {
            if (visited.Contains(s) || !visiting.Add(s))
            {
                return;
            }

            foreach (var typeArgument in s.TypeArguments)
            {
                visitDependency(typeArgument, s);
            }

            foreach (var member in StructAbiMembers.Get(s))
            {
                visitDependency(member.Type, s);
            }

            // The other type of a conversion comes first: the conversion functions after the struct use its value, and
            // a ref struct also names it in the conversion members it declares
            foreach (var member in s.GetMembers())
            {
                if (member is not IMethodSymbol
                    {
                        MethodKind: MethodKind.Conversion, IsImplicitlyDeclared: false
                    } conversion)
                {
                    continue;
                }

                visitDependency(conversion.ReturnType, s);
                foreach (var parameter in conversion.Parameters)
                {
                    visitDependency(parameter.Type, s);
                }
            }

            visiting.Remove(s);
            visited.Add(s);
            result.Add(s);
        }

        // [WHY] The owner itself is skipped: visiting it again while it is being visited would return at once anyway.
        void visitDependency(ITypeSymbol type, INamedTypeSymbol owner)
        {
            if (TryGetUserStruct(type, out var dep) && !dep.IsEqualTo(owner) && structSet.Contains(dep))
            {
                visit(dep);
            }
        }
    }

    private static bool TryGetUserStruct(ITypeSymbol? typeSymbol, [NotNullWhen(true)] out INamedTypeSymbol? namedStruct)
    {
        // [WHY] Array-like wrappers (T[], Memory<T>, ImmutableArray<T>, ...) reach the device as GSharpArray<T>; only
        // their element type can be a user struct.
        var structType = typeSymbol.TryGetArrayLikeElementType(out var elementType) ? elementType : typeSymbol;
        if (structType is INamedTypeSymbol candidate && GlobalCppNames.IsEmittedUserType(candidate))
        {
            namedStruct = candidate;
            return true;
        }

        namedStruct = null;
        return false;
    }
}
