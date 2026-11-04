
namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Entry point for discovering user structs and generating their host marshaling infrastructure.</summary>
internal static class StructMarshalingEmitter
{
    /// <summary>The runtime type that the CUDA launcher passes to <c>Marshal_</c> as <c>deviceArrays</c>.</summary>
    internal const string DeviceArrayRegionsType = "global::Kassyi.GSharp.Runtime.Interop.DeviceArrayRegions";
    internal const string CudaArraysParameter = "deviceArrays";
    internal const string CpuArraysParameter = "allocatedPtrs";
    internal const string MirroredHostParameter = "hostValue";

    /// <summary>Emits the rendered structs of a kernel class (<see cref="RenderStructs"/>).</summary>
    /// <remarks>A struct whose <c>AotContexts</c> name is already taken gets no helpers; GSH0058 reports it.</remarks>
    internal static void WriteStructMarshaling(SourceWriter writer, EquatableArray<StructHostSource> structs, bool isCuda)
    {
        if (structs.Length > 0)
        {
            writer.AppendLine(
                "#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type");
            foreach (var s in structs)
            {
                writer.Append(s.GetText(isCuda));
            }

            writer.AppendLine("#pragma warning restore CS8500");
        }
    }

    /// <summary>
    /// Renders the native shadow struct and the marshaling methods of each custom struct of <paramref name="kernel"/>'s
    /// parameters and return type, one per <c>AotContexts</c> name (<see cref="CollectUniqueStructs"/>).
    /// </summary>
    /// <remarks>
    /// The element of an array has CPU text only when the CPU launcher converts it; an Identical element is passed as
    /// the pinned array.
    /// </remarks>
    internal static EquatableArray<StructHostSource> RenderStructs(IMethodSymbol kernel)
    {
        var structTypes = CollectUniqueStructs([kernel], null);
        var cpuStructs = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        kernel.Parameters
            .AsStackEnumerable()
            .Do(
                cpuStructs,
                static (parameter, structs) => CollectParameterStructs(parameter.Type, HostDialects.Cpu, structs)
            );
        CollectReturnStruct(kernel.ReturnType, HostDialects.Cpu, cpuStructs);

        var sources = ImmutableArray.CreateBuilder<StructHostSource>(structTypes.Length);
        foreach (var s in structTypes)
        {
            sources.Add(new StructHostSource(
                AotHostNames.GetAotStructPath(s),
                RenderStructForDialect(s, HostDialects.Cuda),
                cpuStructs.Contains(s) ? RenderStructForDialect(s, HostDialects.Cpu) : string.Empty));
        }

        return sources.MoveToImmutable().AsEquatableArray();
    }

    private static string RenderStructForDialect(INamedTypeSymbol s, ITranspilerDialect dialect)
    {
        var structLayout = TypeLayoutCalculator.GetStructLayout(s, dialect);
        using var writer = new SourceWriter();
        NativeStructEmitter.WriteNativeStructDeclaration(writer, s, structLayout);
        StructMarshalMethodEmitter.WriteMarshalMethod(writer, s, structLayout, dialect);
        StructUnmarshalMethodEmitter.WriteUnmarshalMethod(writer, s, structLayout, dialect);
        if (!dialect.IsCuda)
        {
            StructArrayHelperEmitter.WriteCopyArraysMethod(writer, s, structLayout);
            StructArrayHelperEmitter.WriteHoldsArrayMethods(writer, s, structLayout);
        }

        return writer.ToString();
    }

    /// <summary>
    /// Collects the custom structs of the kernel signatures, one per <c>AotContexts</c> name. A struct that maps to a
    /// name that another struct already has is skipped and, when <paramref name="duplicates"/> is given, recorded.
    /// </summary>
    /// <remarks>
    /// Two constructions of one generic struct (<c>Box&lt;int&gt;</c>, <c>Box&lt;long&gt;</c>) are also duplicates:
    /// the native declaration is not generic, so each construction needs its own layout under the same name.
    /// </remarks>
    internal static ImmutableArray<INamedTypeSymbol> CollectUniqueStructs(IEnumerable<IMethodSymbol> kernels,
        ImmutableArray<(INamedTypeSymbol Kept, INamedTypeSymbol Duplicate, string Name)>.Builder? duplicates)
    {
        var structTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var kernel in kernels)
        {
            kernel.Parameters
                .AsStackEnumerable()
                .Do(structTypes, static (parameter, structs) => CollectParameterStructs(parameter.Type, structs));
            CollectReturnStruct(kernel.ReturnType, HostDialects.Cuda, structTypes);
            CollectReturnStruct(kernel.ReturnType, HostDialects.Cpu, structTypes);
        }

        var byName = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        var unique = ImmutableArray.CreateBuilder<INamedTypeSymbol>(structTypes.Count);
        foreach (var s in structTypes)
        {
            var name = AotHostNames.GetAotStructPath(s);
            if (!byName.TryGetValue(name, out var kept))
            {
                byName.Add(name, s);
                unique.Add(s);
            }
            else
            {
                duplicates?.Add((kept, s, name));
            }
        }

        return unique.ToImmutable();
    }

    // An array-like parameter whose element a launcher (CUDA or CPU) converts goes through the element's shadow
    private static void CollectParameterStructs(ITypeSymbol type, HashSet<INamedTypeSymbol> structs)
    {
        var marshaledType = type.UnwrapNullableArrayLike() ?? type;
        TypeLayoutCalculator.CollectStructs(
            marshaledType.TryGetArrayLikeElementType(out var element) &&
            (KernelParameterShape.IsConvertedOnCuda(element) || KernelParameterShape.IsConverted(element, HostDialects.Cpu))
                ? element
                : type,
            structs);
    }

    // The same for one dialect: the element of an array that this dialect's launcher converts
    private static void CollectParameterStructs(ITypeSymbol type, ITranspilerDialect dialect,
        HashSet<INamedTypeSymbol> structs)
    {
        var marshaledType = type.UnwrapNullableArrayLike() ?? type;
        TypeLayoutCalculator.CollectStructs(
            marshaledType.TryGetArrayLikeElementType(out var element) && KernelParameterShape.IsConverted(element, dialect)
                ? element
                : type,
            structs);
    }

    // [WHY] The auto result array of a kernel that returns a user struct is read back like an [Out] array: element by
    // element through Unmarshal_ when the struct lays out differently on the dialect's device (#704), otherwise as it is
    private static void CollectReturnStruct(ITypeSymbol returnType, ITranspilerDialect dialect,
        HashSet<INamedTypeSymbol> structs)
    {
        if (KernelParameterShape.IsConvertedReturn(returnType, dialect))
        {
            TypeLayoutCalculator.CollectStructs(returnType, structs);
        }
    }

}
