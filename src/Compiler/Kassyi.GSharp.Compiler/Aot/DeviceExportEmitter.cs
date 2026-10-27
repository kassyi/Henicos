using Kassyi.GSharp.Compiler.Models;

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
    /// A struct fails for a dialect only when its own parts do (<see cref="StructSourcePart.Failed"/>), not with the
    /// function or the struct whose transpilation wrote it.
    /// </remarks>
    public static EquatableArray<DeviceExportRecord> CreateStructRecords(StructSourceCollector cuda,
        StructSourceCollector cpp)
    {
        var types = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        AddTypes(types, cuda.Definitions);
        AddTypes(types, cuda.Discovered);
        AddTypes(types, cpp.Definitions);
        AddTypes(types, cpp.Discovered);

        var records = ImmutableArray.CreateBuilder<DeviceExportRecord>(types.Count);
        foreach (var type in types)
        {
            records.Add(CreateStructRecord(type, ComposeStructText(type, cuda), ComposeStructText(type, cpp)));
        }

        return records.MoveToImmutable().AsEquatableArray();
    }

    private static void AddTypes(ImmutableArray<INamedTypeSymbol>.Builder types,
        ImmutableArray<StructSourcePart>.Builder parts)
    {
        foreach (var part in parts)
        {
            if (!types
                    .AsStackEnumerable()
                    .Any(part.Type, static (seen, t) => seen.IsEqualTo(t)))
            {
                types.Add(part.Type);
            }
        }
    }

    // The text of one struct for one dialect: its definition, its Dispose, and the includes of both
    private static StructDialectText ComposeStructText(INamedTypeSymbol type, StructSourceCollector collector)
    {
        if (!TryFind(collector.Definitions, type, out var own) && !TryFind(collector.Discovered, type, out own))
        {
            return new StructDialectText("", "", true, default, "");
        }

        var includes = new HashSet<string>(StringComparer.Ordinal);
        own.Includes
            .AsStackEnumerable()
            .AddTo(includes);
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

        return new StructDialectText(own.Text, dispose, failed, StructTranspiler.ToIncludes(includes), internalError);
    }

    /// <summary>
    /// The record of one struct from its texts for both dialects. The text of a dialect that failed is left out: the
    /// record says it failed, and a consumer copies none of it.
    /// </summary>
    public static DeviceExportRecord CreateStructRecord(INamedTypeSymbol type, StructDialectText cuda, StructDialectText cpp)
    {
        var internalError = cuda.InternalError.Length > 0 ? cuda.InternalError : cpp.InternalError;
        if (cuda.Failed)
        {
            cuda = new StructDialectText("", "", true, default, "");
        }

        if (cpp.Failed)
        {
            cpp = new StructDialectText("", "", true, default, "");
        }

        var cppName = GlobalCppNames.GetTypeName(type);
        var constructors = ImmutableArray.CreateBuilder<string>();
        type.InstanceConstructors
            .AsStackEnumerable()
            .Where(StructTranspiler.IsEmittedStructConstructor)
            .Select(GlobalCppNames.GetConstructorName)
            .AddTo(constructors);

        var definedNames = constructors.ToImmutable().AsEquatableArray();
        return new DeviceExportRecord(
            DeviceExportRecord.StructKind,
            type.GetDocumentationCommentId() ?? cppName,
            cppName,
            GlobalCppNames.TryGetFullName(cppName, out var fullName) ? fullName : "",
            definedNames,
            CollectReferences(OwnNames(cppName, definedNames), cuda.Definition, cuda.Dispose),
            CollectReferences(OwnNames(cppName, definedNames), cpp.Definition, cpp.Dispose),
            "",
            default,
            Suspends: false,
            ReachesBlockBarrier: false,
            IsParallelDependent: false,
            ReachesDynamicSharedMemory: false,
            default,
            "Memory",
            "",
            cuda.Definition,
            cuda.Dispose,
            "",
            cpp.Definition,
            cpp.Dispose,
            cuda.Includes,
            cpp.Includes,
            cuda.Failed || cuda.Definition.Length == 0,
            cpp.Failed || cpp.Definition.Length == 0,
            ManagedStateMachine: false,
            internalError);
    }

    /// <summary>The names that a text defines itself: its own name and the names it defines.</summary>
    public static EquatableArray<string> OwnNames(string cppName, EquatableArray<string> definedNames)
    {
        var names = ImmutableArray.CreateBuilder<string>();
        names.Add(cppName);
        definedNames
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
            structsByName[record.CppName] = structsByName.TryGetValue(record.CppName, out var existing)
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
        if (Prefer(candidate.CudaFailed, candidate.CudaDispose, existing.CudaFailed, existing.CudaDispose))
        {
            merged = merged with
            {
                CudaBody = candidate.CudaBody, CudaDispose = candidate.CudaDispose, CudaFailed = false,
                CudaReferences = candidate.CudaReferences, CudaIncludes = candidate.CudaIncludes
            };
        }

        if (Prefer(candidate.CppFailed, candidate.CppDispose, existing.CppFailed, existing.CppDispose))
        {
            merged = merged with
            {
                CppBody = candidate.CppBody, CppDispose = candidate.CppDispose, CppFailed = false,
                CppReferences = candidate.CppReferences, CppIncludes = candidate.CppIncludes
            };
        }

        // The message of a record whose failure the merged record keeps
        var internalError = "";
        if (merged.CudaFailed)
        {
            internalError = existing.CudaFailed && existing.InternalError.Length > 0 ? existing.InternalError
                : candidate.CudaFailed ? candidate.InternalError : "";
        }

        if (internalError.Length == 0 && merged.CppFailed)
        {
            internalError = existing.CppFailed && existing.InternalError.Length > 0 ? existing.InternalError
                : candidate.CppFailed ? candidate.InternalError : "";
        }

        return merged with { InternalError = internalError };
    }

    private static bool Prefer(bool candidateFailed, string candidateDispose, bool existingFailed, string existingDispose) =>
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
            AppendStringProperty(writer, nameof(DeviceExportRecord.CppName), record.CppName);
            AppendStringProperty(writer, nameof(DeviceExportRecord.CppFullName), record.CppFullName);
            AppendArrayProperty(writer, nameof(DeviceExportRecord.DefinedNames), record.DefinedNames);
            AppendArrayProperty(writer, nameof(DeviceExportRecord.CudaReferences), record.CudaReferences);
            AppendArrayProperty(writer, nameof(DeviceExportRecord.CppReferences), record.CppReferences);
            AppendStringProperty(writer, nameof(DeviceExportRecord.FrameName), record.FrameName);
            AppendArrayProperty(writer, nameof(DeviceExportRecord.FrameDependencies), record.FrameDependencies);
            AppendBoolProperty(writer, nameof(DeviceExportRecord.Suspends), record.Suspends);
            AppendBoolProperty(writer, nameof(DeviceExportRecord.ReachesBlockBarrier), record.ReachesBlockBarrier);
            AppendBoolProperty(writer, nameof(DeviceExportRecord.IsParallelDependent), record.IsParallelDependent);
            AppendBoolProperty(writer, nameof(DeviceExportRecord.ReachesDynamicSharedMemory),
                record.ReachesDynamicSharedMemory);
            AppendArrayProperty(writer, nameof(DeviceExportRecord.DynamicSharedMemorySites),
                record.DynamicSharedMemorySites);
            AppendStringProperty(writer, nameof(DeviceExportRecord.Effect), record.Effect);
            AppendStringProperty(writer, nameof(DeviceExportRecord.CudaSignature), record.CudaSignature);
            AppendStringProperty(writer, nameof(DeviceExportRecord.CudaBody), record.CudaBody);
            AppendStringProperty(writer, nameof(DeviceExportRecord.CudaDispose), record.CudaDispose);
            AppendStringProperty(writer, nameof(DeviceExportRecord.CppSignature), record.CppSignature);
            AppendStringProperty(writer, nameof(DeviceExportRecord.CppBody), record.CppBody);
            AppendStringProperty(writer, nameof(DeviceExportRecord.CppDispose), record.CppDispose);
            AppendArrayProperty(writer, nameof(DeviceExportRecord.CudaIncludes), record.CudaIncludes);
            AppendArrayProperty(writer, nameof(DeviceExportRecord.CppIncludes), record.CppIncludes);
            AppendBoolProperty(writer, nameof(DeviceExportRecord.CudaFailed), record.CudaFailed);
            AppendBoolProperty(writer, nameof(DeviceExportRecord.CppFailed), record.CppFailed);
            AppendBoolProperty(writer, nameof(DeviceExportRecord.ManagedStateMachine), record.ManagedStateMachine);
            AppendStringProperty(writer, nameof(DeviceExportRecord.InternalError), record.InternalError);
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

/// <summary>The text of one struct for one dialect, as its record stores it.</summary>
/// <param name="Definition">Its definition.</param>
/// <param name="Dispose">Its <c>Dispose</c> definition, or an empty string.</param>
/// <param name="Failed">Whether the dialect could not transpile it.</param>
/// <param name="Includes">The <c>#include</c> targets of the text.</param>
/// <param name="InternalError">The message of an exception that stopped writing it, or an empty string.</param>
internal readonly record struct StructDialectText(
    string Definition,
    string Dispose,
    bool Failed,
    EquatableArray<string> Includes,
    string InternalError);
