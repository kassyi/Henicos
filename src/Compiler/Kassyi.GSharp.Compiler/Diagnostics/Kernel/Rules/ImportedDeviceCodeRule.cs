using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports a use of device code of another assembly that the native module cannot import: a <c>[GpuDeviceFunction]</c>,
/// or a struct's constructor or computed property, whose assembly recorded nothing for it (GSH0088), recorded it for
/// another G# or failed to transpile it (GSH0089), or gave its C++ name to something else too (GSH0090); and the same for
/// every record that its text reaches through the names it refers to, reported at the use with the dependency's name.
/// </summary>
/// <remarks>
/// [WHY] The consumer's native module copies the text that the declaring assembly recorded, and the records that text
/// refers to (cross-assembly-device-code.md §3.8). Without a usable record the C++ build would fail on an undefined name,
/// a struct constructor would be emitted as an aggregate initialization that skips its body, or a name would bind to
/// another assembly's function; the analyzer stops the build with a G# diagnostic instead. The generator reports
/// nothing itself and names the call as usual.
/// </remarks>
internal sealed class ImportedDeviceCodeRule : ISyntaxValidationRule<InvocationExpressionSyntax>,
    ISyntaxValidationRule<BaseObjectCreationExpressionSyntax>, ISyntaxValidationRule<MemberAccessExpressionSyntax>,
    ISyntaxValidationRule<ParameterSyntax>, ISyntaxValidationRule<LocalDeclarationStatementSyntax>,
    ISyntaxValidationRule<VariableDeclarationSyntax>
{
    public static readonly ImportedDeviceCodeRule Instance = new();

    private static readonly ConditionalWeakTable<Compilation, ReferenceIndex> s_indices = new();

    private ImportedDeviceCodeRule()
    {
    }

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.DeviceFunctionNotExported,
        GSharpDiagnostics.DeviceExportUnusable,
        GSharpDiagnostics.DeviceExportNameCollision);

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is not IMethodSymbol symbol)
        {
            return;
        }

        // An iterator of another assembly is GSH0003 (it is expanded where it is enumerated, from its body)
        var function = (symbol.ReducedFrom ?? symbol).OriginalDefinition;
        if (!function.HasAttribute(KnownAttributes.GpuDeviceFunction) ||
            !DeviceExportTable.IsOutside(function, context.SemanticModel.Compilation) ||
            IteratorTypeResolver.IsExternalIteratorCandidate(function))
        {
            return;
        }

        var location = node.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.GetLocation(),
            _ => node.Expression.GetLocation()
        };
        Check(function, function, location, ref context);
    }

    // A constructor with a body runs it in C#; the struct's record brings the emitted constructor. A target-typed
    // new(...) is a creation as well; both are reported at their new keyword
    public void Analyze(BaseObjectCreationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is IMethodSymbol { IsImplicitlyDeclared: false } constructor &&
            constructor.ContainingType is { } type && IsImportedStruct(type, context.SemanticModel.Compilation))
        {
            Check(constructor, type, node.NewKeyword.GetLocation(), ref context);
        }
    }

    // A computed property is a member function of the struct's definition, which the struct's record brings
    public void Analyze(MemberAccessExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is IPropertySymbol { IsStatic: false } property &&
            property.ContainingType is { } type && StructAbiMembers.IsComputedProperty(property) &&
            IsImportedStruct(type, context.SemanticModel.Compilation))
        {
            Check(type, type, node.Name.GetLocation(), ref context);
        }
    }

    // A struct used as a type (a local, a parameter, a type argument, an array element, a field of the consumer's own
    // struct): its record is imported by its name (or, when it cannot be, the struct is defined from metadata), so
    // only a C++ name that two references give to the same path is a problem (GSH0090), reported at the written type
    public void Analyze(ParameterSyntax node, ref ValidationContext context)
    {
        if (node.Type is { } type)
        {
            CheckTypeUse(type, ref context);
        }
    }

    private static void CheckTypeUse(TypeSyntax typeSyntax, ref ValidationContext context)
    {
        var compilation = context.SemanticModel.Compilation;
        if (context.SemanticModel.GetTypeInfo(typeSyntax).Type is not { } type)
        {
            return;
        }

        var index = s_indices.GetValue(compilation, static c => new ReferenceIndex(c));
        if (FindTypeCollision(type, compilation, index, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) is
            ({ } colliding, var imported, { } other))
        {
            context.ReportDiagnostic(GSharpDiagnostics.DeviceExportNameCollision, typeSyntax.GetLocation(),
                FormatName(colliding), imported.AssemblyName, other, imported.Record.CppName);
        }
    }

    // The first struct of another assembly that the type is or holds (type arguments, array elements, the instance
    // fields of the compilation's own structs) whose C++ name two references give
    private static (INamedTypeSymbol? Type, ImportedDeviceRecord Record, string? Other) FindTypeCollision(
        ITypeSymbol type, Compilation compilation, ReferenceIndex index, HashSet<ITypeSymbol> visited)
    {
        if (type is IArrayTypeSymbol array)
        {
            return FindTypeCollision(array.ElementType, compilation, index, visited);
        }

        if (type is not INamedTypeSymbol named || !visited.Add(named))
        {
            return default;
        }

        if (IsImportedStruct(named, compilation) && DeviceExportTable.TryFind(named, out var imported) &&
            index.FindCollision(imported) is { } other)
        {
            return (named, imported, other);
        }

        foreach (var argument in named.TypeArguments)
        {
            if (FindTypeCollision(argument, compilation, index, visited) is ({ }, _, { }) found)
            {
                return found;
            }
        }

        if (named.TypeKind != TypeKind.Struct || DeviceExportTable.IsOutside(named, compilation))
        {
            return default;
        }

        foreach (var member in named.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false } field &&
                FindTypeCollision(field.Type, compilation, index, visited) is ({ }, _, { }) found)
            {
                return found;
            }
        }

        return default;
    }

    // using var x = ...: the Dispose of a struct of another assembly comes with its record
    public void Analyze(LocalDeclarationStatementSyntax node, ref ValidationContext context)
    {
        if (node.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
        {
            CheckDisposed(node.Declaration.Type, node.UsingKeyword.GetLocation(), ref context);
        }
    }

    // using (var x = ...) { }
    public void Analyze(VariableDeclarationSyntax node, ref ValidationContext context)
    {
        if (node.Parent is UsingStatementSyntax statement)
        {
            CheckDisposed(node.Type, statement.UsingKeyword.GetLocation(), ref context);
        }

        CheckTypeUse(node.Type, ref context);
    }

    // [WHY] Without a record the transpiler reports the Dispose as GSH0003 (no body in this compilation); with one that
    // cannot be used (another G#, failed in the dialect) this rule reports it, and the transpiler stays silent
    private static void CheckDisposed(TypeSyntax typeSyntax, Location location, ref ValidationContext context)
    {
        var compilation = context.SemanticModel.Compilation;
        if (context.SemanticModel.GetTypeInfo(typeSyntax).Type is INamedTypeSymbol type &&
            IsImportedStruct(type, compilation) && DeviceExportTable.TryFind(type, out _))
        {
            Check(type, type, location, ref context);
        }
    }

    // A struct of another G# assembly (not of Kassyi.GSharp.Core or the BCL, whose types the dialects map)
    private static bool IsImportedStruct(INamedTypeSymbol type, Compilation compilation) =>
        GlobalCppNames.IsEmittedUserType(type) &&
        !type.ContainingNamespace.IsCoreNamespace() &&
        type.ContainingAssembly is { } assembly &&
        !string.Equals(assembly.Name, TypeConstants.CoreNamespace, StringComparison.Ordinal) &&
        DeviceExportTable.IsOutside(type, compilation);

    // symbol is what the record is found by (a function, a struct, a constructor); named is what the message names
    private static void Check(ISymbol symbol, ISymbol named, Location location, ref ValidationContext context)
    {
        var compilation = context.SemanticModel.Compilation;
        var assemblyName = named.ContainingAssembly?.Name ?? "";
        var displayName = FormatName(named);
        if (!DeviceExportTable.TryFind(symbol, out var imported))
        {
            context.ReportDiagnostic(GSharpDiagnostics.DeviceFunctionNotExported, location, displayName, assemblyName);
            return;
        }

        var index = s_indices.GetValue(compilation, static c => new ReferenceIndex(c));
        var problem = index.CheckRecord(imported, displayName, context.Dialect.IsCuda, others: null);
        if (problem.Descriptor == null)
        {
            // The records that the text reaches, each checked as the record of the use
            foreach (var dependency in index.CheckDependencies(imported, context.Dialect.IsCuda))
            {
                ReportProblem(dependency, location, ref context);
            }

            return;
        }

        ReportProblem(problem, location, ref context);
    }

    private static void ReportProblem(Problem problem, Location location, ref ValidationContext context)
    {
        if (problem.Descriptor != null)
        {
            context.ReportDiagnostic(problem.Descriptor, location, problem.Arguments);
        }
    }

    private static string FormatName(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor } constructor => constructor.ContainingType.Name,
        IMethodSymbol method when method.ContainingType != null => method.ContainingType.Name + "." + method.Name,
        _ => symbol.Name
    };

    /// <summary>A diagnostic to report at the use, with its message arguments; none when the descriptor is null.</summary>
    private readonly record struct Problem(DiagnosticDescriptor? Descriptor, object[] Arguments);

    /// <summary>
    /// The records of every assembly that a compilation references, by the C++ names they define, and the problems of
    /// the records that each record's text reaches (per record and dialect, computed once).
    /// </summary>
    private sealed class ReferenceIndex
    {
        private readonly Compilation _compilation;
        private readonly Dictionary<string, ImportedDeviceRecord[]> _byName;
        private readonly ConcurrentDictionary<(string Assembly, string Id, bool IsCuda), ImmutableArray<Problem>> _dependencies = new();

        public ReferenceIndex(Compilation compilation)
        {
            _compilation = compilation;
            var byName = new Dictionary<string, ImmutableArray<ImportedDeviceRecord>.Builder>(StringComparer.Ordinal);
            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                {
                    continue;
                }

                foreach (var imported in DeviceExportTable.ForAssembly(assembly).Records)
                {
                    Add(byName, imported.Record.CppName, imported);
                    foreach (var name in imported.Record.DefinedNames)
                    {
                        Add(byName, name, imported);
                    }
                }
            }

            _byName = new Dictionary<string, ImportedDeviceRecord[]>(byName.Count, StringComparer.Ordinal);
            foreach (var pair in byName)
            {
                _byName[pair.Key] = pair.Value.ToArray();
            }
        }

        /// <summary>
        /// Why <paramref name="imported"/> cannot be used in the dialect: another G#, a failed text, or a C++ name that
        /// another assembly (<paramref name="others"/>, all the records of the name) or the compilation also gives.
        /// </summary>
        public Problem CheckRecord(ImportedDeviceRecord imported, string displayName, bool isCuda,
            ImportedDeviceRecord[]? others)
        {
            var assemblyName = imported.AssemblyName;
            if (!imported.IsCompatible)
            {
                return new Problem(GSharpDiagnostics.DeviceExportUnusable,
                [
                    displayName, assemblyName,
                    $"it was written by another version of G# (record format {imported.FormatVersion}, prelude " +
                    $"{imported.PreludeHash}; this G#: {DeviceExportRecord.CurrentFormatVersion}, " +
                    $"{DeviceExportRecord.CurrentPreludeHash})"
                ]);
            }

            if (imported.FailedFor(isCuda))
            {
                var dialect = isCuda ? "CUDA" : "C++";
                return new Problem(GSharpDiagnostics.DeviceExportUnusable,
                [
                    displayName, assemblyName,
                    imported.Record.InternalError.Length > 0
                        ? $"the build of '{assemblyName}' could not transpile it for the {dialect} dialect; the generator failed with {imported.Record.InternalError}"
                        : $"the build of '{assemblyName}' could not transpile it for the {dialect} dialect; see the diagnostics of that build"
                ]);
            }

            if (FindCollision(imported, others ?? Find(imported.Record.CppName)) is { } other)
            {
                return new Problem(GSharpDiagnostics.DeviceExportNameCollision,
                    [displayName, assemblyName, other, imported.Record.CppName]);
            }

            return default;
        }

        /// <summary>The problems of the records that the text of <paramref name="imported"/> reaches.</summary>
        public ImmutableArray<Problem> CheckDependencies(ImportedDeviceRecord imported, bool isCuda) =>
            _dependencies.GetOrAdd((imported.AssemblyName, imported.Record.Id, isCuda),
                key => Walk(imported, key.IsCuda));

        private ImmutableArray<Problem> Walk(ImportedDeviceRecord start, bool isCuda)
        {
            var problems = ImmutableArray.CreateBuilder<Problem>();
            var visited = new HashSet<(string, string)>
            {
                (start.AssemblyName, start.Record.Id)
            };
            var pending = new Queue<(string Name, ImportedDeviceRecord Referrer)>();
            foreach (var name in start.Record.GetReferences(isCuda))
            {
                pending.Enqueue((name, start));
            }

            var reported = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                var (name, referrer) = pending.Dequeue();
                var candidates = Find(name);
                if (candidates.Length == 0)
                {
                    if (reported.Add(name))
                    {
                        var (display, assembly) = DescribeUnresolved(name, referrer.AssemblyName);
                        problems.Add(new Problem(GSharpDiagnostics.DeviceFunctionNotExported, [display, assembly]));
                    }

                    continue;
                }

                var chosen = Choose(candidates, referrer.AssemblyName);
                if (!visited.Add((chosen.AssemblyName, chosen.Record.Id)))
                {
                    continue;
                }

                var problem = CheckRecord(chosen, DescribeRecord(chosen.Record), isCuda, candidates);
                if (problem.Descriptor != null)
                {
                    problems.Add(problem);
                    continue;
                }

                foreach (var reference in chosen.Record.GetReferences(isCuda))
                {
                    pending.Enqueue((reference, chosen));
                }
            }

            return problems.ToImmutable();
        }

        private ImportedDeviceRecord[] Find(string name) => _byName.TryGetValue(name, out var records) ? records : [];

        // The record of the referring assembly first, as the import does (DeviceImportAssembler.Resolve)
        private static ImportedDeviceRecord Choose(ImportedDeviceRecord[] candidates, string assembly)
        {
            foreach (var candidate in candidates)
            {
                if (string.Equals(candidate.AssemblyName, assembly, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }

        /// <summary>The assembly (or this compilation) that gives the record's C++ name to another declaration.</summary>
        public string? FindCollision(ImportedDeviceRecord imported) => FindCollision(imported, Find(imported.Record.CppName));

        // The assembly (or this compilation) that gives the record's C++ name to another declaration, or null
        private string? FindCollision(ImportedDeviceRecord imported, ImportedDeviceRecord[] records)
        {
            foreach (var other in records)
            {
                if (!string.Equals(other.AssemblyName, imported.AssemblyName, StringComparison.Ordinal))
                {
                    return other.AssemblyName;
                }
            }

            // The compilation declares the same path: its own device code takes the same name
            return DeviceExportTable.IsDeclaredIn(imported.Record, _compilation) ? _compilation.AssemblyName ?? "" : null;
        }

        // The C# name of a record, from its symbol when the compilation sees it, else from its ID (an internal function
        // of a metadata reference is not imported)
        private string DescribeRecord(DeviceExportRecord record)
        {
            if (DocumentationCommentId.GetFirstSymbolForDeclarationId(DeviceExportTable.ToDefinitionId(record.Id),
                    _compilation) is { } symbol)
            {
                return FormatName(symbol);
            }

            var id = record.Id.Length > 2 && record.Id[1] == ':' ? record.Id[2..] : record.Id;
            var end = id.IndexOfAny(['(', '{', '`']);
            var path = end >= 0 ? id[..end] : id;
            return LastSegments(path, record.Kind == DeviceExportRecord.FunctionKind ? 2 : 1);
        }

        // The C# name of a C++ name that no record defines, and the assembly that declares its type when the compilation
        // sees it (else the assembly whose record refers to it)
        private (string Display, string Assembly) DescribeUnresolved(string name, string referrer)
        {
            if (!GSharpNameEncoding.TryDecode(name, out var path) || path.Length == 0)
            {
                return (name, referrer);
            }

            // The longest prefix of the path that names a type: the function's type, or the type itself
            var display = LastSegments(path, name.StartsWith(GSharpNameEncoding.FunctionPrefix, StringComparison.Ordinal) ? 2 : 1);
            for (var end = path.Length; end > 0; end = path.LastIndexOf('.', end - 1))
            {
                if (_compilation.GetTypeByMetadataName(path[..end]) is { ContainingAssembly: { } assembly })
                {
                    return (display, assembly.Name);
                }
            }

            return (display, referrer);
        }

        private static string LastSegments(string path, int count)
        {
            var start = path.Length;
            for (var i = 0; i < count && start > 0; i++)
            {
                start = path.LastIndexOf('.', start - 1);
                if (start < 0)
                {
                    return path;
                }
            }

            return path[(start + 1)..];
        }

        private static void Add(Dictionary<string, ImmutableArray<ImportedDeviceRecord>.Builder> byName, string name,
            ImportedDeviceRecord imported)
        {
            if (!byName.TryGetValue(name, out var list))
            {
                list = ImmutableArray.CreateBuilder<ImportedDeviceRecord>();
                byName[name] = list;
            }

            list.Add(imported);
        }
    }
}
