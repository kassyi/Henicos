using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The device code export records (<c>[assembly: GpuDeviceExport]</c>) of one referenced assembly, read from its
/// metadata, and the questions a consumer asks about a symbol of another assembly: its record, its C++ name, its flags.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] A symbol of another assembly has no body in this compilation, so the analyses that walk bodies (suspension,
/// dynamic shared memory, effects, grid scope) and the names that need the declaration order (overloads <c>_o{n}</c>)
/// read what the declaring assembly recorded instead (cross-assembly-device-code.md §3.5). The records live in the
/// assembly that declares the symbol, so the table is found from the symbol alone, without the compilation; it is read
/// once per assembly symbol (<see cref="ConditionalWeakTable{TKey, TValue}"/>, as <c>BclIntrinsicMapBuilder</c> does).
/// </para>
/// <para>
/// "Another assembly" is a symbol without a declaration in the compilation (<see cref="IsOutside"/>): a metadata
/// reference, or the source of a referenced compilation in an IDE.
/// </para>
/// </remarks>
internal sealed class DeviceExportTable
{
    private static readonly ConditionalWeakTable<IAssemblySymbol, DeviceExportTable> s_tables = new();

    private static readonly DeviceExportTable s_empty = new("", ImmutableArray<ImportedDeviceRecord>.Empty);

    private readonly Dictionary<string, ImportedDeviceRecord> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImportedDeviceRecord> _byName = new(StringComparer.Ordinal);

    private DeviceExportTable(string assemblyName, ImmutableArray<ImportedDeviceRecord> records)
    {
        AssemblyName = assemblyName;
        Records = records;
        // A constructor is found through its struct (TryFind): a generic struct's constructors share one ID across the
        // constructed structs, whose records differ
        foreach (var imported in records)
        {
            _byId[imported.Record.Id] = imported;
            _byName[imported.Record.CppName] = imported;
            foreach (var name in imported.Record.DefinedNames)
            {
                _byName[name] = imported;
            }
        }
    }

    /// <summary>The name of the assembly.</summary>
    public string AssemblyName { get; }

    /// <summary>The records of the assembly, in the order of its attributes.</summary>
    public ImmutableArray<ImportedDeviceRecord> Records { get; }

    /// <summary>Whether the assembly exports device code at all.</summary>
    public bool HasRecords => !Records.IsEmpty;

    /// <summary>The table of <paramref name="assembly"/>; empty for an assembly that exports nothing.</summary>
    public static DeviceExportTable ForAssembly(IAssemblySymbol? assembly) =>
        assembly == null ? s_empty : s_tables.GetValue(assembly, static a => Read(a));

    /// <summary>
    /// Whether <paramref name="symbol"/> has no declaration in <paramref name="compilation"/>: it comes from a metadata
    /// reference, or from the source of a referenced compilation (an IDE's project reference).
    /// </summary>
    public static bool IsOutside(ISymbol symbol, Compilation compilation) =>
        !symbol.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(compilation, static (reference, c) => c.ContainsSyntaxTree(reference.SyntaxTree));

    /// <summary>
    /// The record of <paramref name="symbol"/> (a device function, a struct, or a struct constructor) in the assembly
    /// that declares it. A constructor's is the record of its struct as constructed (<c>Box&lt;float&gt;</c>, not
    /// <c>Box&lt;int&gt;</c>), when that record lists the constructor (<see cref="DeviceExportRecord.DefinedIds"/>).
    /// </summary>
    public static bool TryFind(ISymbol symbol, out ImportedDeviceRecord imported)
    {
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.Conversion, ContainingType: { } structType } structMethod)
        {
            return TryFind(structType, out imported) &&
                   imported.Record.DefinedIds
                       .AsStackEnumerable()
                       .Any(structMethod.OriginalDefinition.GetDocumentationCommentId(),
                           static (id, own) => string.Equals(id, own, StringComparison.Ordinal));
        }

        var definition = symbol is IMethodSymbol method ? (method.ReducedFrom ?? method).OriginalDefinition : symbol.OriginalDefinition;
        var id = (symbol is INamedTypeSymbol type ? type : definition).GetDocumentationCommentId();
        if (id != null && ForAssembly(definition.ContainingAssembly)._byId.TryGetValue(id, out imported))
        {
            return true;
        }

        imported = default;
        return false;
    }

    /// <summary>
    /// The record of a <c>[GpuDeviceFunction]</c> of another assembly than <paramref name="compilation"/>; false for one
    /// of the compilation, or one whose assembly recorded nothing for it.
    /// </summary>
    public static bool TryFindFunction(IMethodSymbol method, Compilation compilation, out ImportedDeviceRecord imported)
    {
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        if (definition.HasAttribute(KnownAttributes.GpuDeviceFunction) && IsOutside(definition, compilation) &&
            TryFind(definition, out imported) && imported.Record.Kind == DeviceExportRecord.FunctionKind)
        {
            return true;
        }

        imported = default;
        return false;
    }

    /// <summary>The C++ name that the declaring assembly recorded for a function, struct or constructor of metadata.</summary>
    /// <remarks>
    /// Used for the symbols without any declaration (metadata), whose declaration order (<c>_o{n}</c>) only the records
    /// keep; a referenced compilation's symbols have their declarations and are named as in their own assembly.
    /// </remarks>
    public static bool TryGetRecordedName(ISymbol symbol, [NotNullWhen(true)] out string? cppName)
    {
        cppName = null;
        if (!symbol.DeclaringSyntaxReferences.IsEmpty || !TryFind(symbol, out var imported))
        {
            return false;
        }

        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.Conversion } structMethod)
        {
            var id = structMethod.OriginalDefinition.GetDocumentationCommentId();
            for (var i = 0; i < imported.Record.DefinedIds.Length && i < imported.Record.DefinedNames.Length; i++)
            {
                if (string.Equals(imported.Record.DefinedIds[i], id, StringComparison.Ordinal))
                {
                    cppName = imported.Record.DefinedNames[i];
                    return true;
                }
            }

            return false;
        }

        cppName = imported.Record.CppName;
        return true;
    }

    /// <summary>The record that defines the C++ name <paramref name="cppName"/> (a function, struct or constructor).</summary>
    public bool TryGetByName(string cppName, out ImportedDeviceRecord imported) => _byName.TryGetValue(cppName, out imported);

    /// <summary>
    /// The ID of the declaration that <paramref name="id"/> names, as <see cref="DocumentationCommentId"/> looks it up:
    /// the definition of a constructed struct (<c>T:N.Box{System.Int32}</c> is a construction of <c>T:N.Box`1</c>);
    /// any other ID (a method's braces are its parameter types) as it is.
    /// </summary>
    public static string ToDefinitionId(string id)
    {
        if (!id.StartsWith("T:", StringComparison.Ordinal) || id.IndexOf('{') < 0)
        {
            return id;
        }

        var builder = new StringBuilder(id.Length);
        var depth = 0;
        var arity = 0;
        foreach (var c in id)
        {
            switch (c)
            {
                case '{':
                    depth++;
                    arity = depth == 1 ? 1 : arity;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        builder.Append('`').Append(arity);
                    }

                    break;
                case ',' when depth == 1:
                    arity++;
                    break;
                default:
                    if (depth == 0)
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether <paramref name="compilation"/> declares the path of <paramref name="record"/> itself: its own device code
    /// takes the same C++ name (the names follow the C# path, <see cref="GlobalCppNames"/>).
    /// </summary>
    public static bool IsDeclaredIn(DeviceExportRecord record, Compilation compilation)
    {
        foreach (var symbol in DocumentationCommentId.GetSymbolsForDeclarationId(ToDefinitionId(record.Id), compilation))
        {
            if (!IsOutside(symbol, compilation))
            {
                return true;
            }
        }

        return false;
    }

    private static DeviceExportTable Read(IAssemblySymbol assembly)
    {
        var records = ImmutableArray.CreateBuilder<ImportedDeviceRecord>();
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: KnownAttributes.GpuDeviceExport + "Attribute" } attributeClass &&
                attributeClass.ContainingNamespace.IsCoreNamespace() &&
                TryRead(attribute, assembly.Name, out var imported))
            {
                records.Add(imported);
            }
        }

        return records.Count == 0 ? s_empty : new DeviceExportTable(assembly.Name, records.ToImmutable());
    }

    private static bool TryRead(AttributeData attribute, string assemblyName, out ImportedDeviceRecord imported)
    {
        imported = default;
        var arguments = attribute.ConstructorArguments;
        if (arguments.Length != 4 ||
            arguments[0].Value is not int formatVersion ||
            arguments[1].Value is not string preludeHash ||
            arguments[2].Value is not string kind ||
            arguments[3].Value is not string id)
        {
            return false;
        }

        var named = new Dictionary<string, TypedConstant>(StringComparer.Ordinal);
        foreach (var pair in attribute.NamedArguments)
        {
            named[pair.Key] = pair.Value;
        }

        var record = new DeviceExportRecord(
            kind,
            id,
            GetString(named, nameof(DeviceExportRecord.CppName)),
            GetString(named, nameof(DeviceExportRecord.CppFullName)),
            GetArray(named, nameof(DeviceExportRecord.DefinedNames)),
            GetArray(named, nameof(DeviceExportRecord.DefinedIds)),
            GetArray(named, nameof(DeviceExportRecord.DefinedFullNames)),
            GetArray(named, nameof(DeviceExportRecord.CudaReferences)),
            GetArray(named, nameof(DeviceExportRecord.CppReferences)),
            GetString(named, nameof(DeviceExportRecord.FrameName)),
            GetArray(named, nameof(DeviceExportRecord.FrameDependencies)),
            GetBool(named, nameof(DeviceExportRecord.Suspends)),
            GetBool(named, nameof(DeviceExportRecord.ReachesBlockBarrier)),
            GetBool(named, nameof(DeviceExportRecord.IsParallelDependent)),
            GetBool(named, nameof(DeviceExportRecord.ReachesDynamicSharedMemory)),
            GetArray(named, nameof(DeviceExportRecord.DynamicSharedMemorySites)),
            named.TryGetValue(nameof(DeviceExportRecord.Effect), out var effect) && effect.Value is string e ? e : "Memory",
            GetString(named, nameof(DeviceExportRecord.CudaSignature)),
            GetString(named, nameof(DeviceExportRecord.CudaBody)),
            GetString(named, nameof(DeviceExportRecord.CudaDispose)),
            GetString(named, nameof(DeviceExportRecord.CppSignature)),
            GetString(named, nameof(DeviceExportRecord.CppBody)),
            GetString(named, nameof(DeviceExportRecord.CppDispose)),
            GetArray(named, nameof(DeviceExportRecord.CudaIncludes)),
            GetArray(named, nameof(DeviceExportRecord.CppIncludes)),
            GetBool(named, nameof(DeviceExportRecord.CudaFailed)),
            GetBool(named, nameof(DeviceExportRecord.CppFailed)),
            GetBool(named, nameof(DeviceExportRecord.ManagedStateMachine)),
            GetBool(named, nameof(DeviceExportRecord.ManagedVersion)),
            GetString(named, nameof(DeviceExportRecord.InternalError)),
            GetInt(named, nameof(DeviceExportRecord.DivergentConditionParameters)),
            GetInt(named, nameof(DeviceExportRecord.DivergentExitOnlyParameters)),
            GetBool(named, nameof(DeviceExportRecord.HasExitOnlyBarrier)));

        imported = new ImportedDeviceRecord(record, assemblyName, formatVersion, preludeHash);
        return record.CppName.Length > 0;
    }

    private static string GetString(Dictionary<string, TypedConstant> named, string name) =>
        named.TryGetValue(name, out var value) && value.Value is string text ? text : "";

    private static bool GetBool(Dictionary<string, TypedConstant> named, string name) =>
        named.TryGetValue(name, out var value) && value.Value is true;

    private static int GetInt(Dictionary<string, TypedConstant> named, string name) =>
        named.TryGetValue(name, out var value) && value.Value is int number ? number : 0;

    private static EquatableArray<string> GetArray(Dictionary<string, TypedConstant> named, string name)
    {
        if (!named.TryGetValue(name, out var value) || value.Kind != TypedConstantKind.Array || value.Values.IsDefault)
        {
            return default;
        }

        var builder = ImmutableArray.CreateBuilder<string>(value.Values.Length);
        value.Values
            .AsStackEnumerable()
            .Select(static item => item.Value as string ?? "")
            .AddTo(builder);
        return builder.MoveToImmutable().AsEquatableArray();
    }
}

/// <summary>A record read from a referenced assembly, with what the consumer checks before using it.</summary>
/// <param name="Record">The record.</param>
/// <param name="AssemblyName">The assembly that declares the function or struct.</param>
/// <param name="FormatVersion">The record layout the assembly was built with.</param>
/// <param name="PreludeHash">The hash of the preludes the text was written for.</param>
internal readonly record struct ImportedDeviceRecord(
    DeviceExportRecord Record,
    string AssemblyName,
    int FormatVersion,
    string PreludeHash)
{
    /// <summary>
    /// Whether the text was written by this G#: the same record layout, preludes and internal names
    /// (<see cref="DeviceExportRecord.CurrentPreludeHash"/>).
    /// </summary>
    public bool IsCompatible =>
        FormatVersion == DeviceExportRecord.CurrentFormatVersion &&
        string.Equals(PreludeHash, DeviceExportRecord.CurrentPreludeHash, StringComparison.Ordinal);

    /// <summary>Whether the library's build could not transpile the text of a dialect.</summary>
    public bool FailedFor(bool isCuda) => isCuda ? Record.CudaFailed : Record.CppFailed;
}
