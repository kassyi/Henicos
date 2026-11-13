using Kassyi.GSharp.Compiler.Models;
using AttributeNames = Kassyi.GSharp.Compiler.Models.DeviceExportAttributeNames;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>
/// Builds the device code export records of an assembly (<see cref="DeviceExportRecord"/>) and writes them as
/// <c>[assembly: GpuDeviceExport(...)]</c> attributes, which another assembly reads from metadata.
/// </summary>
/// <remarks>
/// [WHY] The transpiled C++ of a device function exists in the assembly that declares it; a kernel of another assembly
/// that calls it must place the same text in its own translation unit, so that nvcc and cl inline across the boundary
/// as within one assembly. A record holds text and names only; assembly attributes are the one channel that a
/// generator can read from a reference without file I/O (as <c>BclIntrinsicMapBuilder</c> reads
/// <c>[assembly: BclIntrinsicMap]</c>).
/// </remarks>
internal static class DeviceExportEmitter
{
    /// <summary>The hint name of the generated file; shared plumbing, so the generator snapshots leave it out.</summary>
    public const string HintName = "GSharpSharedDeviceExports.g.cs";

    private static readonly string[] s_referencePrefixes =
    [
        GSharpNameEncoding.FunctionPrefix,
        GSharpNameEncoding.TypePrefix,
        GSharpInternalNames.Lowering.ConstructorPrefix
    ];

    /// <summary>
    /// The global C++ names (<c>__gsharp_fn_</c>, <c>__gsharp_ty_</c>, <c>__gsharp_ctor_</c>) that the texts refer to,
    /// in ordinal order without duplicates, leaving out <paramref name="ownNames"/>.
    /// </summary>
    /// <remarks>
    /// [WHY] The names are read from the text rather than from the syntax, so a reference that only the transpiler adds
    /// (an inlined iterator body, a struct constructor, a field type) is found as well: what the text names is what a
    /// consumer must define before it.
    /// </remarks>
    public static EquatableArray<string> CollectReferences(EquatableArray<string> ownNames, params string[] texts)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            var index = 0;
            while ((index = text.IndexOf("__gsharp_", index, StringComparison.Ordinal)) >= 0)
            {
                var start = index;
                var end = start;
                while (end < text.Length && IsIdentifierChar(text[end]))
                {
                    end++;
                }

                index = end;
                if (start > 0 && IsIdentifierChar(text[start - 1]))
                {
                    continue;
                }

                var name = text[start..end];
                if (StartsWithReferencePrefix(name) &&
                    !ownNames
                        .AsStackEnumerable()
                        .Any(name, static (own, n) => string.Equals(own, n, StringComparison.Ordinal)))
                {
                    names.Add(name);
                }
            }
        }

        var builder = ImmutableArray.CreateBuilder<string>(names.Count);
        names
            .AsStackEnumerable()
            .AddTo(builder);
        builder.Sort(StringComparer.Ordinal);
        return builder.MoveToImmutable().AsEquatableArray();
    }

    /// <summary>
    /// The struct records of one transpilation (a method, or a struct exported on its own), from the struct parts that
    /// the CUDA and the C++ runs collected (<see cref="StructSourceCollector"/>). A struct whose text comes from a
    /// dialect run that failed is marked failed for that dialect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the structs that <paramref name="compilation"/> declares are recorded (<see cref="IsOwnStruct"/>): the
    /// records of a struct live in the assembly that declares it, so that two libraries never record one struct.
    /// </para>
    /// <para>
    /// [WHY] A struct that no G# assembly records (<see cref="IsUnrecordedStruct"/>: a Kassyi.GSharp.Core vector
    /// type, a struct of a referenced assembly without G#) has no record a consumer could import, so its definition
    /// stays in the text of every record that names it, in front of that record's own definition (#538 review B1).
    /// Its include guard keeps the copies from redefining it. A struct of another G# library stays a reference: the
    /// consumer imports that library's record, with the members (computed properties, Dispose) that a definition from
    /// metadata lacks.
    /// </para>
    /// <para>
    /// A struct fails for a dialect only when its own parts do (<see cref="StructSourcePart.Failed"/>), not with the
    /// function or the struct whose transpilation wrote it.
    /// </para>
    /// </remarks>
    public static EquatableArray<DeviceExportRecord> CreateStructRecords(Compilation compilation,
        StructSourceCollector cuda, StructSourceCollector cpp)
    {
        var types = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        AddRecordedTypes(types, cuda.Definitions, compilation);
        AddRecordedTypes(types, cuda.Discovered, compilation);
        AddRecordedTypes(types, cpp.Definitions, compilation);
        AddRecordedTypes(types, cpp.Discovered, compilation);

        var records = ImmutableArray.CreateBuilder<DeviceExportRecord>(types.Count);
        foreach (var type in types)
        {
            records.Add(CreateStructRecord(type, compilation,
                ComposeStructText(type, cuda, compilation, isCuda: true),
                ComposeStructText(type, cpp, compilation, isCuda: false)));
        }

        return records.MoveToImmutable().AsEquatableArray();
    }

    /// <summary>Whether <paramref name="compilation"/> declares <paramref name="type"/>, so its records hold it.</summary>
    public static bool IsOwnStruct(INamedTypeSymbol type, Compilation compilation) =>
        !DeviceExportTable.IsOutside(type.OriginalDefinition, compilation);

    /// <summary>
    /// Whether no G# assembly records <paramref name="type"/> for the dialect: it is declared outside
    /// <paramref name="compilation"/>, and its assembly has no record of it (a Kassyi.GSharp.Core vector type, a struct
    /// of an assembly without G#, a construction of a generic struct that its library does not use), or one that the
    /// dialect cannot use (written by another G#, or failed in the dialect: a consumer would not import it).
    /// </summary>
    public static bool IsUnrecordedStruct(INamedTypeSymbol type, Compilation compilation, bool isCuda) =>
        DeviceExportTable.IsOutside(type.OriginalDefinition, compilation) &&
        (!DeviceExportTable.TryFind(type, out var imported) || !imported.IsCompatible || imported.FailedFor(isCuda));

    /// <summary>
    /// The definitions and <c>Dispose</c> definitions of <paramref name="parts"/> whose structs no G# assembly
    /// records (<see cref="IsUnrecordedStruct"/>), in their order, with the C++ names of those structs: a function
    /// record keeps them in front of its body.
    /// </summary>
    public static string KeepUnrecorded(ImmutableArray<StructSourcePart>.Builder parts, Compilation compilation,
        bool isCuda, ImmutableArray<string>.Builder names)
    {
        using var text = new SourceWriter();
        foreach (var part in parts)
        {
            if (IsUnrecordedStruct(part.Type, compilation, isCuda))
            {
                text.Append(part.Text);
                names.Add(GlobalCppNames.GetTypeName(part.Type));
            }
        }

        return text.ToString();
    }

    private static void AddRecordedTypes(ImmutableArray<INamedTypeSymbol>.Builder types,
        ImmutableArray<StructSourcePart>.Builder parts, Compilation compilation)
    {
        foreach (var part in parts)
        {
            if (IsOwnStruct(part.Type, compilation) &&
                !types
                    .AsStackEnumerable()
                    .Any(part.Type, static (seen, t) => seen.IsEqualTo(t)))
            {
                types.Add(part.Type);
            }
        }
    }

    // The text of one struct for one dialect: the definitions of the unrecorded structs that its definition or its
    // Dispose names (directly or through them), then its own definition; its Dispose; the includes of all of them
    private static StructDialectText ComposeStructText(INamedTypeSymbol type, StructSourceCollector collector,
        Compilation compilation, bool isCuda)
    {
        if (!TryFind(collector.Definitions, type, out var own) && !TryFind(collector.Discovered, type, out own))
        {
            return new StructDialectText("", "", true, default, default, "");
        }

        var unrecorded = new Dictionary<string, StructSourcePart>(StringComparer.Ordinal);
        AddUnrecorded(unrecorded, collector.Definitions, compilation, isCuda);
        AddUnrecorded(unrecorded, collector.Discovered, compilation, isCuda);

        using var definition = new SourceWriter();
        var includes = new HashSet<string>(StringComparer.Ordinal);
        var kept = ImmutableArray.CreateBuilder<string>();
        var failed = own.Failed;
        var internalError = own.InternalError;
        var dispose = "";
        if (TryFind(collector.Disposes, type, out var disposePart))
        {
            dispose = disposePart.Text;
            disposePart.Includes
                .AsStackEnumerable()
                .AddTo(includes);
            failed |= disposePart.Failed;
            internalError = internalError.Length > 0 ? internalError : disposePart.InternalError;
        }

        foreach (var name in CollectReferences(default, own.Text, dispose))
        {
            KeepUnrecordedDependency(name, unrecorded, definition, includes, kept, ref failed);
        }

        definition.Append(own.Text);
        own.Includes
            .AsStackEnumerable()
            .AddTo(includes);

        return new StructDialectText(definition.ToString(), dispose, failed, StructTranspiler.ToIncludes(includes),
            kept.ToImmutable().AsEquatableArray(), internalError);
    }

    // An unrecorded struct after the unrecorded structs that its own text names
    private static void KeepUnrecordedDependency(string name, Dictionary<string, StructSourcePart> unrecorded,
        SourceWriter definition, HashSet<string> includes, ImmutableArray<string>.Builder kept, ref bool failed)
    {
        if (!unrecorded.TryGetValue(name, out var part) || kept.Contains(name))
        {
            return;
        }

        kept.Add(name);
        foreach (var dependency in CollectReferences(default, part.Text))
        {
            KeepUnrecordedDependency(dependency, unrecorded, definition, includes, kept, ref failed);
        }

        definition.Append(part.Text);
        part.Includes
            .AsStackEnumerable()
            .AddTo(includes);
        failed |= part.Failed;
    }

    private static void AddUnrecorded(Dictionary<string, StructSourcePart> unrecorded,
        ImmutableArray<StructSourcePart>.Builder parts, Compilation compilation, bool isCuda)
    {
        foreach (var part in parts)
        {
            if (IsUnrecordedStruct(part.Type, compilation, isCuda))
            {
                var name = GlobalCppNames.GetTypeName(part.Type);
                if (!unrecorded.ContainsKey(name))
                {
                    unrecorded[name] = part;
                }
            }
        }
    }

    /// <summary>
    /// The record of one struct from its texts for both dialects. The text of a dialect that failed is left out: the
    /// record says it failed, and a consumer copies none of it.
    /// </summary>
    public static DeviceExportRecord CreateStructRecord(INamedTypeSymbol type, Compilation compilation,
        StructDialectText cuda, StructDialectText cpp)
    {
        var internalError = cuda.InternalError.Length > 0 ? cuda.InternalError : cpp.InternalError;
        if (cuda.Failed)
        {
            cuda = new StructDialectText("", "", true, default, default, "");
        }

        if (cpp.Failed)
        {
            cpp = new StructDialectText("", "", true, default, default, "");
        }

        var cppName = GlobalCppNames.GetTypeName(type);
        var names = CreateStructNames(type, compilation, cppName);
        return new DeviceExportRecord(
            DeviceExportRecord.StructKind,
            type.GetDocumentationCommentId() ?? cppName,
            names,
            BuildStructDialectText(cppName, names.DefinedNames, cuda),
            BuildStructDialectText(cppName, names.DefinedNames, cpp),
            DeviceExportRecord.FunctionTraits.None,
            internalError);
    }

    // The names of a struct and of the functions defined with it: the emitted constructors, conversions and operators
    // true and false
    private static DeviceExportRecord.ExportNames CreateStructNames(INamedTypeSymbol type, Compilation compilation,
        string cppName)
    {
        var definedFunctions = ImmutableArray.CreateBuilder<string>();
        var definedFunctionIds = ImmutableArray.CreateBuilder<string>();
        var definedFunctionFullNames = ImmutableArray.CreateBuilder<string>();
        foreach (var constructor in type.InstanceConstructors)
        {
            if (StructConstructorEmitter.IsEmittedStructConstructor(constructor, compilation))
            {
                var name = GlobalCppNames.GetConstructorName(constructor);
                definedFunctions.Add(name);
                definedFunctionIds.Add(constructor.OriginalDefinition.GetDocumentationCommentId() ?? "");
                definedFunctionFullNames.Add(GlobalCppNames.TryGetFullName(name, out var full) ? full : "");
            }
        }

        foreach (var member in type.GetMembers())
        {
            // A conversion, or an operator true or false, defined with the struct
            if (member is IMethodSymbol method &&
                (StructConversionEmitter.IsEmittedStructConversion(method, compilation) ||
                 StructTruthOperatorEmitter.IsEmittedTruthOperator(method, compilation)))
            {
                var name = GlobalCppNames.GetFunctionName(method);
                definedFunctions.Add(name);
                definedFunctionIds.Add(method.OriginalDefinition.GetDocumentationCommentId() ?? "");
                definedFunctionFullNames.Add(GlobalCppNames.TryGetFullName(name, out var full) ? full : "");
            }
        }

        return new DeviceExportRecord.ExportNames(
            CppName: cppName,
            CppFullName: GlobalCppNames.TryGetFullName(cppName, out var fullName) ? fullName : "",
            DefinedNames: definedFunctions.ToImmutable().AsEquatableArray(),
            DefinedIds: definedFunctionIds.ToImmutable().AsEquatableArray(),
            DefinedFullNames: definedFunctionFullNames
                .AsStackEnumerable()
                .Any(static n => n.Length > 0)
                ? definedFunctionFullNames.ToImmutable().AsEquatableArray()
                : default);
    }

    private static DeviceExportRecord.DialectText BuildStructDialectText(
        string cppName,
        EquatableArray<string> definedNames,
        StructDialectText text) =>
        new(
            CollectReferences(OwnNames(cppName, definedNames, text.KeptNames), text.Definition, text.Dispose),
            Signature: "",
            Body: text.Definition,
            Dispose: text.Dispose,
            text.Includes,
            Failed: text.Failed || text.Definition.Length == 0);

    /// <summary>The names that a text defines itself: its own name, the names it defines, the structs it keeps.</summary>
    public static EquatableArray<string> OwnNames(string cppName, EquatableArray<string> definedNames,
        EquatableArray<string> keptNames)
    {
        var names = ImmutableArray.CreateBuilder<string>();
        names.Add(cppName);
        definedNames
            .AsStackEnumerable()
            .AddTo(names);
        keptNames
            .AsStackEnumerable()
            .AddTo(names);
        return names.ToImmutable().AsEquatableArray();
    }

    /// <summary>The name of an effect as a record stores it.</summary>
    public static string FormatEffect(DeviceFunctionEffect effect) => effect switch
    {
        DeviceFunctionEffect.None => "None",
        DeviceFunctionEffect.RefParameters => "RefParameters",
        _ => "Memory"
    };

    /// <summary>
    /// Writes <c>GSharpSharedDeviceExports.g.cs</c>: one <c>[assembly: GpuDeviceExport(...)]</c> per record, functions
    /// first, then structs, each in the order of its ID. A struct that several methods use is written once; a text from
    /// a dialect run that did not fail is preferred.
    /// </summary>
    public static string Generate(IReadOnlyList<DeviceExportRecord> functions, IReadOnlyList<DeviceExportRecord> structs,
        string preludeHash)
    {
        var sortedFunctions = ImmutableArray.CreateBuilder<DeviceExportRecord>(functions.Count);
        functions
            .AsStackEnumerable()
            .AddTo(sortedFunctions);
        sortedFunctions.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));

        var structsByName = new SortedDictionary<string, DeviceExportRecord>(StringComparer.Ordinal);
        foreach (var record in structs)
        {
            structsByName[record.Names.CppName] = structsByName.TryGetValue(record.Names.CppName, out var existing)
                ? Merge(existing, record)
                : record;
        }

        using var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader(enableNullable: false);
        foreach (var record in sortedFunctions)
        {
            WriteRecord(writer, record, preludeHash);
        }

        foreach (var record in structsByName.Values)
        {
            WriteRecord(writer, record, preludeHash);
        }

        return writer.ToString();
    }

    // Two records of one struct (written by two transpilations): each dialect's text from a record that did not fail
    // it, one with the Dispose text when both did not (a method that does not dispose the struct writes none)
    private static DeviceExportRecord Merge(DeviceExportRecord existing, DeviceExportRecord candidate)
    {
        var merged = existing;
        if (ShouldPrefer(candidate.Cuda.Failed, candidate.Cuda.Dispose, existing.Cuda.Failed, existing.Cuda.Dispose))
        {
            merged = merged with { Cuda = candidate.Cuda with { Failed = false } };
        }

        if (ShouldPrefer(candidate.Cpp.Failed, candidate.Cpp.Dispose, existing.Cpp.Failed, existing.Cpp.Dispose))
        {
            merged = merged with { Cpp = candidate.Cpp with { Failed = false } };
        }

        // The message of a record whose failure the merged record keeps
        var internalError = "";
        if (merged.Cuda.Failed)
        {
            internalError = existing.Cuda.Failed && existing.InternalError.Length > 0 ? existing.InternalError
                : candidate.Cuda.Failed ? candidate.InternalError : "";
        }

        if (internalError.Length == 0 && merged.Cpp.Failed)
        {
            internalError = existing.Cpp.Failed && existing.InternalError.Length > 0 ? existing.InternalError
                : candidate.Cpp.Failed ? candidate.InternalError : "";
        }

        return merged with { InternalError = internalError };
    }

    private static bool ShouldPrefer(bool candidateFailed, string candidateDispose, bool existingFailed,
        string existingDispose) =>
        !candidateFailed && (existingFailed || (existingDispose.Length == 0 && candidateDispose.Length > 0));

    // [assembly: global::Kassyi.GSharp.Core.GpuDeviceExport(<version>, "<hash>", "<kind>", "<id>", CppName = "<name>", ...)]
    private static void WriteRecord(SourceWriter writer, in DeviceExportRecord record, string preludeHash)
    {
        writer.Append($"[assembly: global::{KnownAttributes.GpuDeviceExportMetadataName}");
        using (writer.Parentheses())
        {
            writer.Append($"{DeviceExportRecord.CurrentFormatVersion}, ");
            AppendString(writer, preludeHash);
            writer.Append(", ");
            AppendString(writer, record.Kind);
            writer.Append(", ");
            AppendString(writer, record.Id);
            var names = record.Names;
            var traits = record.Traits;
            AppendStringProperty(writer, AttributeNames.Names.CppName, names.CppName);
            AppendStringProperty(writer, AttributeNames.Names.CppFullName, names.CppFullName);
            AppendArrayProperty(writer, AttributeNames.Names.DefinedNames, names.DefinedNames);
            AppendArrayProperty(writer, AttributeNames.Names.DefinedIds, names.DefinedIds);
            AppendArrayProperty(writer, AttributeNames.Names.DefinedFullNames, names.DefinedFullNames);
            AppendArrayProperty(writer, AttributeNames.Cuda.References, record.Cuda.References);
            AppendArrayProperty(writer, AttributeNames.Cpp.References, record.Cpp.References);
            AppendStringProperty(writer, AttributeNames.Traits.FrameName, traits.FrameName);
            AppendArrayProperty(writer, AttributeNames.Traits.FrameDependencies, traits.FrameDependencies);
            AppendBoolProperty(writer, AttributeNames.Traits.Suspends, traits.Suspends);
            AppendBoolProperty(writer, AttributeNames.Traits.ReachesBlockBarrier, traits.ReachesBlockBarrier);
            AppendBoolProperty(writer, AttributeNames.Traits.IsParallelDependent, traits.IsParallelDependent);
            AppendBoolProperty(writer, AttributeNames.Traits.ReachesDynamicSharedMemory,
                traits.ReachesDynamicSharedMemory);
            AppendArrayProperty(writer, AttributeNames.Traits.DynamicSharedMemorySites,
                traits.DynamicSharedMemorySites);
            AppendStringProperty(writer, AttributeNames.Traits.Effect, traits.Effect);
            AppendStringProperty(writer, AttributeNames.Cuda.Signature, record.Cuda.Signature);
            AppendStringProperty(writer, AttributeNames.Cuda.Body, record.Cuda.Body);
            AppendStringProperty(writer, AttributeNames.Cuda.Dispose, record.Cuda.Dispose);
            AppendStringProperty(writer, AttributeNames.Cpp.Signature, record.Cpp.Signature);
            AppendStringProperty(writer, AttributeNames.Cpp.Body, record.Cpp.Body);
            AppendStringProperty(writer, AttributeNames.Cpp.Dispose, record.Cpp.Dispose);
            AppendArrayProperty(writer, AttributeNames.Cuda.Includes, record.Cuda.Includes);
            AppendArrayProperty(writer, AttributeNames.Cpp.Includes, record.Cpp.Includes);
            AppendBoolProperty(writer, AttributeNames.Cuda.Failed, record.Cuda.Failed);
            AppendBoolProperty(writer, AttributeNames.Cpp.Failed, record.Cpp.Failed);
            AppendBoolProperty(writer, AttributeNames.Traits.ManagedStateMachine, traits.ManagedStateMachine);
            AppendBoolProperty(writer, AttributeNames.Traits.ManagedVersion, traits.ManagedVersion);
            AppendStringProperty(writer, AttributeNames.InternalError, record.InternalError);
            AppendIntProperty(writer, AttributeNames.Traits.DivergentConditionParameters,
                traits.DivergentConditionParameters);
            AppendIntProperty(writer, AttributeNames.Traits.DivergentExitOnlyParameters,
                traits.DivergentExitOnlyParameters);
            AppendBoolProperty(writer, AttributeNames.Traits.HasExitOnlyBarrier, traits.HasExitOnlyBarrier);
            AppendBoolProperty(writer, AttributeNames.Traits.ReachesArrivalBarrier, traits.ReachesArrivalBarrier);
            AppendBoolProperty(writer, AttributeNames.Traits.ReachesSharedMemory, traits.ReachesSharedMemory);
            // [WHY] Written whenever ManagedVersion is, also as false: a reader takes a record without the key for one of
            // an older G#, whose ManagedVersion meant exactly this trait (DeviceExportTable)
            if (traits.ManagedVersion || traits.ReachesGeneratedMeaning)
            {
                writer.Append($", {AttributeNames.Traits.ReachesGeneratedMeaning} = ");
                writer.Append(traits.ReachesGeneratedMeaning ? "true" : "false");
            }
        }

        writer.AppendLine("]");
    }

    // Empty strings, empty arrays and false are the attribute's defaults, so they are left out
    private static void AppendStringProperty(SourceWriter writer, string name, string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        writer.Append($", {name} = ");
        AppendString(writer, value);
    }

    private static void AppendBoolProperty(SourceWriter writer, string name, bool value) =>
        writer.AppendIf(value, $", {name} = true");

    private static void AppendIntProperty(SourceWriter writer, string name, int value) =>
        writer.AppendIf(value != 0, $", {name} = {value}");

    private static void AppendArrayProperty(SourceWriter writer, string name, EquatableArray<string> values)
    {
        if (values.IsDefaultOrEmpty)
        {
            return;
        }

        writer.Append($", {name} = new string[] ");
        using (writer.Braces())
        {
            for (var i = 0; i < values.Length; i++)
            {
                writer.AppendIf(i > 0, ", ");
                AppendString(writer, values[i]);
            }
        }
    }

    // @"<text>"
    private static void AppendString(SourceWriter writer, string value)
    {
        writer.Append("@\"");
        writer.AppendVerbatimLiteral(value);
        writer.Append('"');
    }

    private static bool StartsWithReferencePrefix(string name) =>
        s_referencePrefixes
            .AsStackEnumerable()
            .Any(name, static (prefix, n) => n.StartsWith(prefix, StringComparison.Ordinal) && n.Length > prefix.Length);

    private static bool TryFind(ImmutableArray<StructSourcePart>.Builder parts, INamedTypeSymbol type,
        out StructSourcePart found) =>
        parts
            .AsStackEnumerable()
            .TryFirst(type, static (part, target) => part.Type.IsEqualTo(target), out found);

    private static bool IsIdentifierChar(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';
}
