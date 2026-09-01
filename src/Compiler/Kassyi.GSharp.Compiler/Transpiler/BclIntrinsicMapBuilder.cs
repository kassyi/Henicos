
namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Discovers assembly attributes and builds immutable mappings from BCL methods to CUDA intrinsic functions.</summary>
/// <remarks><para>Implements: <b>GEN-39</b></para></remarks>
/// <remarks><para>Implements: <b>GEN-16, BCL-01, BCL-02, BCL-03, GEN-30</b></para></remarks>
internal static class BclIntrinsicMapBuilder
{
    private const string BclIntrinsicMapAttributeFullName = "Kassyi.GSharp.Core.BclIntrinsicMapAttribute";

    private static readonly ConditionalWeakTable<Compilation, ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>>
        s_cache =
            new();

    public static ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> GetOrBuildMap(Compilation compilation) =>
        s_cache.GetValue(compilation, static c => BuildMap(c));

    private static ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> BuildMap(Compilation compilation)
    {
        var attributeSymbol = compilation.GetTypeByMetadataName(BclIntrinsicMapAttributeFullName);
        if (attributeSymbol == null)
        {
            return ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>.Empty;
        }

        var builder =
            ImmutableDictionary.CreateBuilder<IMethodSymbol, IntrinsicMappingInfo>(SymbolEqualityComparer.Default);
        var typeCache = new Dictionary<string, INamedTypeSymbol?>(StringComparer.Ordinal);

        foreach (var assembly in GetCandidateAssemblies(compilation))
        {
            if (!ReferencesAssembly(assembly, TypeConstants.CoreNamespace))
            {
                continue;
            }

            foreach (var attribute in assembly.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol) ||
                    attribute.ConstructorArguments.Length != 4 ||
                    attribute.ConstructorArguments[0].Value is not string bclTypeFullName ||
                    attribute.ConstructorArguments[1].Value is not string methodName ||
                    attribute.ConstructorArguments[3].Value is not string cudaIntrinsicName ||
                    string.IsNullOrEmpty(bclTypeFullName) ||
                    string.IsNullOrEmpty(methodName) ||
                    string.IsNullOrEmpty(cudaIntrinsicName))
                {
                    continue;
                }

                var parameterTypesArray = attribute.ConstructorArguments[2].Values;

                if (!typeCache.TryGetValue(bclTypeFullName, out var bclType))
                {
                    bclType = compilation.GetTypeByMetadataName(bclTypeFullName);
                    typeCache[bclTypeFullName] = bclType;
                }

                if (bclType == null)
                {
                    continue;
                }

                foreach (var member in bclType.GetMembers(methodName))
                {
                    if (member is not IMethodSymbol method || !IsMatch(compilation, typeCache, method, parameterTypesArray))
                    {
                        continue;
                    }

                    string? castFirstArgTo = null;
                    foreach (var namedArg in attribute.NamedArguments)
                    {
                        if (namedArg is { Key: "CastFirstArgumentTo", Value.Value: string castVal })
                        {
                            castFirstArgTo = castVal;
                        }
                    }

                    if (bclTypeFullName == "System.Numerics.BitOperations" &&
                        parameterTypesArray.Length > 0 &&
                        parameterTypesArray[0].Value is "System.UIntPtr" or "System.IntPtr")
                    {
                        var is32Bit = compilation.Options.Platform is Platform.X86 or Platform.Arm;
                        if (is32Bit)
                        {
                            cudaIntrinsicName = cudaIntrinsicName.Replace("_64", "_32");
                            if (cudaIntrinsicName == "__popcll")
                            {
                                cudaIntrinsicName = "__popc";
                            }

                            castFirstArgTo = "unsigned int";
                        }
                        else
                        {
                            castFirstArgTo = "unsigned long long int";
                        }
                    }

                    builder[method.OriginalDefinition] =
                        new IntrinsicMappingInfo(cudaIntrinsicName, castFirstArgTo);
                }
            }
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<IAssemblySymbol> GetCandidateAssemblies(Compilation compilation)
    {
        yield return compilation.Assembly;

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol asmSym)
            {
                yield return asmSym;
            }
        }
    }

    private static bool ReferencesAssembly(IAssemblySymbol assembly, string targetAssemblyName)
    {
        if (assembly.Name == targetAssemblyName)
        {
            return true;
        }

        foreach (var module in assembly.Modules)
        {
            foreach (var refAsm in module.ReferencedAssemblies)
            {
                if (refAsm.Name == targetAssemblyName)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsMatch(Compilation compilation, Dictionary<string, INamedTypeSymbol?> typeCache,
        IMethodSymbol method, ImmutableArray<TypedConstant> expectedParameterTypes)
    {
        if (method.Parameters.Length != expectedParameterTypes.Length)
        {
            return false;
        }

        for (var i = 0; i < expectedParameterTypes.Length; i++)
        {
            if (expectedParameterTypes[i].Value is not string expectedTypeName)
            {
                return false;
            }

            if (!typeCache.TryGetValue(expectedTypeName, out var expectedType))
            {
                expectedType = compilation.GetTypeByMetadataName(expectedTypeName);
                typeCache[expectedTypeName] = expectedType;
            }

            if (expectedType == null)
            {
                return false;
            }

            if (!SymbolEqualityComparer.Default.Equals(method.Parameters[i].Type, expectedType))
            {
                return false;
            }
        }

        return true;
    }
}

