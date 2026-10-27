using Kassyi.GSharp.Compiler.Models;
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>The device code that a native module imports from other assemblies, in the order the module needs it.</summary>
/// <param name="Source">The structs (definitions, then <c>Dispose</c> definitions), then the functions (declarations, then definitions).</param>
/// <param name="Includes">The <c>#include</c> targets of the imported text.</param>
/// <param name="FullNames">The full name of each imported name that its assembly shortened, for the symbol file.</param>
internal readonly record struct ImportedDeviceCode(
    string Source,
    EquatableArray<string> Includes,
    EquatableArray<(string Name, string FullName)> FullNames)
{
    /// <summary>The full name of <paramref name="name"/>, when it is an imported name that was shortened.</summary>
    public string? FindFullName(string name)
    {
        foreach (var (shortened, full) in FullNames)
        {
            if (string.Equals(shortened, name, StringComparison.Ordinal))
            {
                return full;
            }
        }

        return null;
    }
}

/// <summary>
/// Places the device code of other assemblies in this assembly's native module: the records
/// (<see cref="DeviceExportTable"/>) that the module's own text reaches, through the names they refer to, in front of
/// its own device code (cross-assembly-device-code.md §3.4).
/// </summary>
/// <remarks>
/// [WHY] The text goes into the same translation unit, so nvcc and cl inline across the assembly boundary as within one
/// assembly; only the records that the module reaches are copied, so a reference to a large library costs what is used
/// (Stdlib is about 21 s of nvcc as a whole). The records are found by the C++ names in the text, the same way the
/// library recorded what each record refers to (<see cref="DeviceExportEmitter.CollectReferences"/>).
/// </remarks>
internal static class DeviceImportAssembler
{
    /// <summary>
    /// The records of every assembly that <paramref name="compilation"/> references, written by this G# (the others
    /// are the analyzer's GSH0089), in the order of the references. A value of the pipeline, equal across edits that keep
    /// the references.
    /// </summary>
    /// <param name="compilation">The compilation.</param>
    /// <param name="excludedAssemblies">
    /// Assembly names, separated by <c>;</c>, whose records are not imported (<c>GSharpDeviceImportExclude</c>).
    /// </param>
    public static EquatableArray<ImportedDeviceRecord> Collect(Compilation compilation, string excludedAssemblies = "")
    {
        var records = ImmutableArray.CreateBuilder<ImportedDeviceRecord>();
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly &&
                !IsExcluded(assembly.Name, excludedAssemblies))
            {
                DeviceExportTable.ForAssembly(assembly).Records
                    .AsStackEnumerable()
                    .Where(compilation, static (imported, c) => imported.IsCompatible && !IsDeclaredIn(imported.Record, c))
                    .AddTo(records);
            }
        }

        return records.ToImmutable().AsEquatableArray();
    }

    private static bool IsExcluded(string assemblyName, string excludedAssemblies)
    {
        var rest = excludedAssemblies.AsSpan();
        while (rest.Length > 0)
        {
            var separator = rest.IndexOf(';');
            var name = (separator < 0 ? rest : rest[..separator]).Trim();
            if (name.SequenceEqual(assemblyName.AsSpan()))
            {
                return true;
            }

            rest = separator < 0 ? ReadOnlySpan<char>.Empty : rest[(separator + 1)..];
        }

        return false;
    }

    // [WHY] A record of a path that the compilation declares itself would be emitted under the same C++ name as the
    // compilation's own text (GSH0090 when the other assembly's one is used); the compilation's own declaration wins
    private static bool IsDeclaredIn(DeviceExportRecord record, Compilation compilation) =>
        DeviceExportTable.IsDeclaredIn(record, compilation);

    /// <summary>The device code of <paramref name="records"/> that <paramref name="ownTexts"/> reach, for one dialect.</summary>
    /// <remarks>
    /// The text is placed as declarations of the functions, then the structs (whose constructors, computed properties
    /// and Dispose bodies may call the functions), then the Dispose definitions, then the function bodies.
    /// </remarks>
    public static ImportedDeviceCode Resolve(EquatableArray<ImportedDeviceRecord> records, bool isCuda,
        IReadOnlyList<string> ownTexts)
    {
        if (records.IsDefaultOrEmpty)
        {
            return new ImportedDeviceCode("", default, default);
        }

        // Every record of a name, in the order of the references; two records of one name in different assemblies are
        // the analyzer's GSH0090
        var byName = new Dictionary<string, ImmutableArray<int>.Builder>(StringComparer.Ordinal);
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i].Record;
            Add(byName, record.CppName, i);
            foreach (var name in record.DefinedNames)
            {
                Add(byName, name, i);
            }
        }

        var reached = Reach(records, byName, ownTexts, isCuda);
        if (reached.Count == 0)
        {
            return new ImportedDeviceCode("", default, default);
        }

        var structs = ImmutableArray.CreateBuilder<DeviceExportRecord>();
        var functions = ImmutableArray.CreateBuilder<DeviceExportRecord>();
        foreach (var index in reached)
        {
            var record = records[index].Record;
            (record.Kind == DeviceExportRecord.StructKind ? structs : functions).Add(record);
        }

        functions.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        structs.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));

        var source = new StringBuilder();
        foreach (var record in functions)
        {
            source.AppendLine(isCuda ? record.CudaSignature : record.CppSignature);
        }

        var orderedStructs = OrderStructs(structs, isCuda);
        foreach (var record in orderedStructs)
        {
            source.Append(isCuda ? record.CudaBody : record.CppBody);
        }

        foreach (var record in orderedStructs)
        {
            source.Append(isCuda ? record.CudaDispose : record.CppDispose);
        }

        AppendFunctionBodies(source, functions, isCuda);

        return new ImportedDeviceCode(source.ToString(), CollectIncludes(structs, functions, isCuda),
            CollectFullNames(structs, functions));
    }

    private static void Add(Dictionary<string, ImmutableArray<int>.Builder> byName, string name, int index)
    {
        if (!byName.TryGetValue(name, out var indices))
        {
            indices = ImmutableArray.CreateBuilder<int>();
            byName[name] = indices;
        }

        indices.Add(index);
    }

    // The indices of the records that the own text reaches, directly or through the records it reaches; a record that
    // failed in the dialect is left out (the analyzer reports its use, GSH0089)
    private static HashSet<int> Reach(EquatableArray<ImportedDeviceRecord> records,
        Dictionary<string, ImmutableArray<int>.Builder> byName, IReadOnlyList<string> ownTexts, bool isCuda)
    {
        var texts = new string[ownTexts.Count];
        for (var i = 0; i < texts.Length; i++)
        {
            texts[i] = ownTexts[i];
        }

        var reached = new HashSet<int>();
        var pending = new Queue<(string Name, string Assembly)>();
        foreach (var name in DeviceExportEmitter.CollectReferences(default, texts))
        {
            pending.Enqueue((name, ""));
        }

        while (pending.Count > 0)
        {
            var (name, assembly) = pending.Dequeue();
            if (!byName.TryGetValue(name, out var indices))
            {
                continue;
            }

            var index = Choose(records, indices, assembly);
            if (records[index].FailedFor(isCuda) || reached.Contains(index) ||
                (records[index].Record.Kind == DeviceExportRecord.StructKind &&
                 !IsUsable(records, byName, index, isCuda, new HashSet<int>())))
            {
                continue;
            }

            reached.Add(index);

            foreach (var reference in records[index].Record.GetReferences(isCuda))
            {
                pending.Enqueue((reference, records[index].AssemblyName));
            }
        }

        return reached;
    }

    // [WHY] A struct record whose text reaches a name that cannot be imported (a record that failed in the dialect, a
    // name that no record defines or that two assemblies define) would make the C++ build fail without a G# diagnostic
    // when the module uses the struct as a type only, which the analyzer does not check (#538 2nd review M2). Such a
    // record is left out: the module defines the struct from metadata, as it did before the import existed. A use of
    // its constructor or computed property is the analyzer's GSH0088 / GSH0089 / GSH0090
    private static bool IsUsable(EquatableArray<ImportedDeviceRecord> records,
        Dictionary<string, ImmutableArray<int>.Builder> byName, int index, bool isCuda, HashSet<int> visiting)
    {
        if (records[index].FailedFor(isCuda))
        {
            return false;
        }

        if (!visiting.Add(index))
        {
            return true;
        }

        foreach (var name in records[index].Record.GetReferences(isCuda))
        {
            if (!byName.TryGetValue(name, out var indices) || DefinedByTwoAssemblies(records, indices) ||
                !IsUsable(records, byName, Choose(records, indices, records[index].AssemblyName), isCuda, visiting))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DefinedByTwoAssemblies(EquatableArray<ImportedDeviceRecord> records,
        ImmutableArray<int>.Builder indices)
    {
        foreach (var index in indices)
        {
            if (!string.Equals(records[index].AssemblyName, records[indices[0]].AssemblyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // [WHY] A record's text was written against the records of its own assembly, so a name it refers to is looked up
    // there first (another assembly with the same name is GSH0090); the module's own text takes the first record
    private static int Choose(EquatableArray<ImportedDeviceRecord> records, ImmutableArray<int>.Builder indices,
        string assembly)
    {
        foreach (var index in indices)
        {
            if (string.Equals(records[index].AssemblyName, assembly, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return indices[0];
    }

    // A struct after the structs whose names its text uses: the types of its fields, and the constructors that its
    // constructors and computed properties call (DefinedNames)
    private static ImmutableArray<DeviceExportRecord> OrderStructs(ImmutableArray<DeviceExportRecord>.Builder structs,
        bool isCuda)
    {
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < structs.Count; i++)
        {
            byName[structs[i].CppName] = i;
            foreach (var name in structs[i].DefinedNames)
            {
                byName[name] = i;
            }
        }

        var ordered = ImmutableArray.CreateBuilder<DeviceExportRecord>(structs.Count);
        var state = new byte[structs.Count];
        for (var i = 0; i < structs.Count; i++)
        {
            Visit(i);
        }

        return ordered.MoveToImmutable();

        void Visit(int index)
        {
            if (state[index] != 0)
            {
                return;
            }

            state[index] = 1;
            foreach (var name in structs[index].GetReferences(isCuda))
            {
                if (byName.TryGetValue(name, out var dependency))
                {
                    Visit(dependency);
                }
            }

            state[index] = 2;
            ordered.Add(structs[index]);
        }
    }

    // On the CPU dialects a frame embeds the frames of its calls by value, so a callee's body comes before its caller's
    // (as NativeModuleAssembler.AssembleDeviceSource orders the own functions)
    private static void AppendFunctionBodies(StringBuilder source, ImmutableArray<DeviceExportRecord>.Builder functions,
        bool isCuda)
    {
        var byFrame = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < functions.Count; i++)
        {
            if (functions[i].FrameName.Length > 0)
            {
                byFrame[functions[i].FrameName] = i;
            }
        }

        var emitted = new bool[functions.Count];
        for (var i = 0; i < functions.Count; i++)
        {
            Append(i);
        }

        void Append(int index)
        {
            if (emitted[index])
            {
                return;
            }

            emitted[index] = true;
            var record = functions[index];
            if (!isCuda)
            {
                foreach (var dependency in record.FrameDependencies)
                {
                    if (byFrame.TryGetValue(dependency, out var dependencyIndex))
                    {
                        Append(dependencyIndex);
                    }
                }
            }

            source.AppendLine(isCuda ? record.CudaBody : record.CppBody);
        }
    }

    private static EquatableArray<string> CollectIncludes(ImmutableArray<DeviceExportRecord>.Builder structs,
        ImmutableArray<DeviceExportRecord>.Builder functions, bool isCuda)
    {
        var includes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in structs)
        {
            (isCuda ? record.CudaIncludes : record.CppIncludes)
                .AsStackEnumerable()
                .AddTo(includes);
        }

        foreach (var record in functions)
        {
            (isCuda ? record.CudaIncludes : record.CppIncludes)
                .AsStackEnumerable()
                .AddTo(includes);
        }

        var builder = ImmutableArray.CreateBuilder<string>(includes.Count);
        includes
            .AsStackEnumerable()
            .AddTo(builder);
        builder.Sort(StringComparer.Ordinal);
        return builder.MoveToImmutable().AsEquatableArray();
    }

    // The full names of the shortened names that the imported text defines: the records' own names and the
    // constructors of the structs
    private static EquatableArray<(string Name, string FullName)> CollectFullNames(
        ImmutableArray<DeviceExportRecord>.Builder structs, ImmutableArray<DeviceExportRecord>.Builder functions)
    {
        var names = ImmutableArray.CreateBuilder<(string Name, string FullName)>();
        foreach (var record in structs)
        {
            AddFullNames(names, record);
        }

        foreach (var record in functions)
        {
            AddFullNames(names, record);
        }

        return names.ToImmutable().AsEquatableArray();
    }

    private static void AddFullNames(ImmutableArray<(string Name, string FullName)>.Builder names,
        DeviceExportRecord record)
    {
        if (record.CppFullName.Length > 0)
        {
            names.Add((record.CppName, record.CppFullName));
        }

        for (var i = 0; i < record.DefinedFullNames.Length && i < record.DefinedNames.Length; i++)
        {
            if (record.DefinedFullNames[i].Length > 0)
            {
                names.Add((record.DefinedNames[i], record.DefinedFullNames[i]));
            }
        }
    }
}
