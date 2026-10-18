
namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Discovers assembly attributes and builds immutable mappings from BCL methods to CUDA intrinsic functions.</summary>
internal static class BclIntrinsicMapBuilder
{
    private static readonly ConditionalWeakTable<Compilation, ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>>
        s_cache =
            new();

    public static ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> GetOrBuildMap(Compilation compilation) =>
        s_cache.GetValue(compilation, static c => BuildMap(c));

    private readonly struct IntrinsicAttributeData(
        string bclTypeFullName,
        string methodName,
        ImmutableArray<TypedConstant> parameterTypes,
        string cudaIntrinsicName,
        string? castFirstArgumentTo)
    {
        public string BclTypeFullName { get; } = bclTypeFullName;
        public string MethodName { get; } = methodName;
        public ImmutableArray<TypedConstant> ParameterTypes { get; } = parameterTypes;
        public string CudaIntrinsicName { get; } = cudaIntrinsicName;
        public string? CastFirstArgumentTo { get; } = castFirstArgumentTo;
    }

    private static ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> BuildMap(Compilation compilation)
    {
        var attributeSymbol = compilation.GetTypeByMetadataName(KnownAttributes.BclIntrinsicMapMetadataName);
        if (attributeSymbol == null)
        {
            return ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>.Empty;
        }

        var builder =
            ImmutableDictionary.CreateBuilder<IMethodSymbol, IntrinsicMappingInfo>(SymbolEqualityComparer.Default);
        var typeCache = new Dictionary<string, INamedTypeSymbol?>(StringComparer.Ordinal);

        foreach (var assembly in GetCandidateAssemblies(compilation))
        {
            if (ReferencesAssembly(assembly, TypeConstants.CoreNamespace))
            {
                ProcessAssemblyAttributes(assembly, attributeSymbol, compilation, typeCache, builder);
            }
        }

        return builder.ToImmutable();
    }

    private static void ProcessAssemblyAttributes(
        IAssemblySymbol assembly,
        INamedTypeSymbol attributeSymbol,
        Compilation compilation,
        Dictionary<string, INamedTypeSymbol?> typeCache,
        ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>.Builder builder)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (TryExtractIntrinsicAttribute(attribute, attributeSymbol, out var data))
            {
                RegisterMethodMappings(data, compilation, typeCache, builder);
            }
        }
    }

    private static bool TryExtractIntrinsicAttribute(
        AttributeData attribute,
        INamedTypeSymbol attributeSymbol,
        out IntrinsicAttributeData data)
    {
        data = default;

        if (!attribute.AttributeClass.IsEqualTo(attributeSymbol) ||
            attribute.ConstructorArguments.Length != 4 ||
            attribute.ConstructorArguments[0].Value is not string bclTypeFullName ||
            attribute.ConstructorArguments[1].Value is not string methodName ||
            attribute.ConstructorArguments[3].Value is not string cudaIntrinsicName ||
            string.IsNullOrEmpty(bclTypeFullName) ||
            string.IsNullOrEmpty(methodName) ||
            string.IsNullOrEmpty(cudaIntrinsicName))
        {
            return false;
        }

        string? castFirstArgTo = null;
        foreach (var namedArg in attribute.NamedArguments)
        {
            if (namedArg is { Key: "CastFirstArgumentTo", Value.Value: string castVal })
            {
                castFirstArgTo = castVal;
            }
        }

        data = new IntrinsicAttributeData(
            bclTypeFullName,
            methodName,
            attribute.ConstructorArguments[2].Values,
            cudaIntrinsicName,
            castFirstArgTo);

        return true;
    }

    private static void RegisterMethodMappings(
        IntrinsicAttributeData data,
        Compilation compilation,
        Dictionary<string, INamedTypeSymbol?> typeCache,
        ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>.Builder builder)
    {
        if (!typeCache.TryGetValue(data.BclTypeFullName, out var bclType))
        {
            bclType = compilation.GetTypeByMetadataName(data.BclTypeFullName);
            typeCache[data.BclTypeFullName] = bclType;
        }

        if (bclType == null)
        {
            return;
        }

        var mapping = AdjustPlatformSpecificMapping(data, compilation);

        foreach (var member in bclType.GetMembers(data.MethodName))
        {
            if (member is IMethodSymbol method &&
                IsMatch(compilation, typeCache, method, data.ParameterTypes))
            {
                builder[method.OriginalDefinition] = mapping;
            }
        }
    }

    private static IntrinsicMappingInfo AdjustPlatformSpecificMapping(
        IntrinsicAttributeData data,
        Compilation compilation)
    {
        var cudaIntrinsicName = data.CudaIntrinsicName;
        var castFirstArgTo = data.CastFirstArgumentTo;

        if (data is { BclTypeFullName: "System.Numerics.BitOperations", ParameterTypes.Length: > 0 } &&
            data.ParameterTypes[0].Value is "System.UIntPtr" or "System.IntPtr")
        {
            var is32Bit = compilation.Options.Platform is Platform.X86 or Platform.Arm;
            if (is32Bit)
            {
                cudaIntrinsicName = cudaIntrinsicName switch
                {
                    "__gsharp_rotate_left_64" => "__gsharp_rotate_left_32",
                    "__gsharp_rotate_right_64" => "__gsharp_rotate_right_32",
                    "__gsharp_trailing_zero_count_64" => "__gsharp_trailing_zero_count_32",
                    "__gsharp_log2_64" => "__gsharp_log2_32",
                    "__gsharp_leading_zero_count_64" => "__gsharp_leading_zero_count_32",
                    "__popcll" => "__popc",
                    _ => cudaIntrinsicName
                };

                castFirstArgTo = "unsigned int";
            }
            else
            {
                castFirstArgTo = "unsigned long long int";
            }
        }

        return new IntrinsicMappingInfo(cudaIntrinsicName, castFirstArgTo);
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

            if (!method.Parameters[i].Type.IsEqualTo(expectedType))
            {
                return false;
            }
        }

        return true;
    }
}

