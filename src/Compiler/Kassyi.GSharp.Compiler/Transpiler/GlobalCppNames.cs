using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Derives the names that share the global C++ scope (types, template parameters, device functions) from their symbols with
/// <see cref="GSharpNameEncoding.Encode"/>, so that distinct C# paths never share one C++ name.
/// </summary>
internal static class GlobalCppNames
{
    /// <summary>Names longer than this are shortened by <see cref="Shorten"/>.</summary>
    public const int ShortenThreshold = 200;

    private const int HashLength = 10;
    private const string HashMarker = "_h";

    // Shortened name -> full name. Only names over the threshold are recorded, so the table stays small.
    private static readonly ConcurrentDictionary<string, string> s_shortenedNames = new(StringComparer.Ordinal);

    /// <summary>
    /// Looks up the unshortened full name for a shortened identifier.
    /// </summary>
    public static bool TryGetFullName(string shortenedName, [NotNullWhen(true)] out string? fullName) =>
        s_shortenedNames.TryGetValue(shortenedName, out fullName);

    // [WHY] A type or function is referenced from many sites; its name is derived once per definition symbol. Source
    // symbols are shared by every semantic model of a compilation, and the table drops them with the compilation.
    private static readonly ConditionalWeakTable<ISymbol, string> s_typeNames = new();
    private static readonly ConditionalWeakTable<ISymbol, string> s_functionNames = new();
    private static readonly ConditionalWeakTable<ISymbol, string> s_functionPathBodies = new();
    private static readonly ConditionalWeakTable<ISymbol, string> s_constructorNames = new();

    /// <summary>
    /// Whether <paramref name="type"/> is a user struct whose definition G# emits itself. Other types
    /// (<c>System_Nullable</c>, shared memory) are defined by the prelude under their legacy spelling.
    /// </summary>
    /// <remarks>
    /// Used for the definition: collecting the structs to define and naming the definition. References use
    /// <see cref="IsEncodedUserType"/>, which also gives way to the dialect's type map. The definition side does not
    /// consult the map, so a mapped user struct would still be defined under its encoded name; no mapped user struct
    /// exists today.
    /// </remarks>
    public static bool IsEmittedUserType(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Struct && !type.ContainingNamespace.IsSystemOrNumerics() && !type.IsSharedMemoryType() && !type.IsWmmaFragmentType() && !type.IsWmmaMarkerType();

    /// <summary>
    /// Whether a reference to <paramref name="type"/> or to one of its instance members uses the encoded name: an
    /// <see cref="IsEmittedUserType">emitted user struct</see> that <paramref name="dialect"/> does not map to a type of
    /// its own.
    /// </summary>
    public static bool IsEncodedUserType(INamedTypeSymbol type, ITranspilerDialect dialect) =>
        IsEmittedUserType(type) && !dialect.IsMappedType(type);

    /// <summary>Returns <c>__gsharp_ty_{path}</c> for <paramref name="type"/>.</summary>
    /// <remarks>
    /// A constructed generic type encodes its type arguments so each specialization is emitted as a distinct
    /// monomorphized struct; an unconstructed generic definition carries its arity (<c>_a{n}</c>).
    /// </remarks>
    public static string GetTypeName(INamedTypeSymbol type) =>
        s_typeNames.GetValue(type, static symbol =>
            Shorten(GSharpNameEncoding.TypePrefix, GSharpNameEncoding.Encode(GetTypePath((INamedTypeSymbol)symbol))));

    /// <summary>
    /// Returns <c>__gsharp_ctor_{type path}</c> for a user-defined struct constructor, with <c>_o{n}</c> when the
    /// struct declares more than one constructor.
    /// </summary>
    public static string GetConstructorName(IMethodSymbol constructor) =>
        s_constructorNames.GetValue(constructor, static symbol =>
        {
            var ctor = (IMethodSymbol)symbol;
            // A constructor of metadata has no declaration order; its assembly recorded the name
            if (DeviceExportTable.TryGetRecordedName(ctor, out var recorded))
            {
                return recorded;
            }

            var body = GSharpNameEncoding.Encode(GetTypePath(ctor.ContainingType));
            var ordinal = GetConstructorOverloadOrdinal(ctor);
            if (ordinal >= 0)
            {
                body += "_o" + ordinal.ToString(CultureInfo.InvariantCulture);
            }

            return Shorten(GSharpInternalNames.Lowering.ConstructorPrefix, body);
        });

    /// <summary>
    /// Returns <c>__gsharp_fn_{type path}__{name}</c> for a device function, with <c>_o{n}</c> when its containing type
    /// declares more than one method with that name.
    /// </summary>
    /// <remarks>
    /// Every overload gets its own C++ name, so the call that C# bound is the call that runs; C++ overload resolution,
    /// which differs from C# (an <c>int</c> argument is ambiguous between <c>long</c> and <c>double</c>), never applies.
    /// </remarks>
    public static string GetFunctionName(IMethodSymbol method) =>
        s_functionNames.GetValue(
            method.MethodKind == MethodKind.Conversion ? method : GetDefinition(method),
            static symbol =>
            {
                var m = (IMethodSymbol)symbol;
                if (m.MethodKind == MethodKind.Conversion &&
                    DeviceExportTable.TryGetRecordedName(m, out var recorded))
                {
                    return recorded;
                }

                return Shorten(GSharpNameEncoding.FunctionPrefix, GetFunctionPathBody(m));
            });

    /// <summary>
    /// Returns <c>__gsharp_k_{type path}__{name}</c>, the exported entry point of a kernel, with <c>_o{n}</c> when its
    /// containing type declares more than one method with that name.
    /// </summary>
    /// <remarks>
    /// This is the only source of the entry point name: the C++ definition, the CUDA <c>GetFunction</c> lookup and the
    /// CPU export lookup all use it, together with <see cref="GetKernelDerivedName"/>. A path that has no symbol emits nothing
    /// that needs these names; it never builds a fallback name from the syntax.
    /// </remarks>
    public static string GetKernelName(IMethodSymbol kernel) =>
        GetKernelDerivedName(GSharpNameEncoding.KernelPrefix, kernel);

    /// <summary>
    /// Returns <paramref name="prefix"/> followed by the body of the entry point name, for the names that belong to one
    /// kernel: the CPU export, the per-thread context and the managed state machine.
    /// </summary>
    public static string GetKernelDerivedName(string prefix, IMethodSymbol kernel) =>
        Shorten(prefix, GetFunctionPathBody(kernel));

    // [WHY] A function of metadata has no declaration order (_o{n}); the body of its name comes from the name its
    // assembly recorded (the full one when it was shortened), so every name derived from it matches that assembly
    private static string GetFunctionPathBody(IMethodSymbol method) =>
        s_functionPathBodies.GetValue(
            method.MethodKind == MethodKind.Conversion ? method : GetDefinition(method),
            static symbol =>
            {
                var m = (IMethodSymbol)symbol;
                return m.DeclaringSyntaxReferences.IsEmpty && DeviceExportTable.TryFind(m, out var imported) &&
                       imported.Record.Kind == DeviceExportRecord.FunctionKind
                    ? GetRecordedBody(imported.Record)
                    : GSharpNameEncoding.Encode(GetFunctionPath(m));
            });

    private static string GetRecordedBody(DeviceExportRecord record)
    {
        var full = record.CppFullName.Length > 0 ? record.CppFullName : record.CppName;
        return full.StartsWith(GSharpNameEncoding.FunctionPrefix, StringComparison.Ordinal)
            ? full[GSharpNameEncoding.FunctionPrefix.Length..]
            : full;
    }

    /// <summary>The C# path of <paramref name="method"/>: the path of its containing type and the method.</summary>
    public static GlobalNamePath GetFunctionPath(IMethodSymbol method)
    {
        var definition = GetDefinition(method);
        var containingType = method.MethodKind == MethodKind.Conversion
            ? method.ContainingType
            : definition.ContainingType;
        var typePath = GetTypePath(containingType);
        var segments = ImmutableArray.CreateBuilder<GlobalNameSegment>(typePath.Segments.Count + 1);
        foreach (var segment in typePath.Segments)
        {
            segments.Add(segment);
        }

        segments.Add(new GlobalNameSegment(definition.Name, arity: definition.Arity,
            overloadOrdinal: GetOverloadOrdinal(definition)));
        return new GlobalNamePath(segments.MoveToImmutable());
    }

    private static IMethodSymbol GetDefinition(IMethodSymbol method)
    {
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        return definition.PartialDefinitionPart ?? definition;
    }

    private static int GetConstructorOverloadOrdinal(IMethodSymbol constructor)
    {
        constructor = GetDefinition(constructor);
        var constructors = constructor.ContainingType.InstanceConstructors;
        var candidates = new ISymbol[constructors.Length];
        var count = 0;
        foreach (var candidate in constructors)
        {
            if (!candidate.IsImplicitlyDeclared &&
                (candidate.TryGetDeclaringSyntax<ConstructorDeclarationSyntax>(out _) || candidate.IsPrimaryConstructor()))
            {
                candidates[count++] = candidate;
            }
        }

        if (count <= 1)
        {
            return -1;
        }

        Array.Sort(candidates, 0, count, DeclarationOrderComparer.Instance);
        var ordinal = candidates
            .AsStackEnumerable()
            .Take(count)
            .IndexOf(constructor, static (candidate, target) => candidate.IsEqualTo(target));
        if (ordinal >= 0)
        {
            return ordinal;
        }

        throw new InvalidOperationException(
            $"The constructor is not a member of '{constructor.ContainingType.Name}'.");
    }

    /// <summary>
    /// The position of <paramref name="method"/> among the ordinary methods with the same name in its containing type,
    /// in declaration order, or -1 when the name is not overloaded.
    /// </summary>
    /// <remarks>Partial types order their parts by file path like file types do.</remarks>
    public static int GetOverloadOrdinal(IMethodSymbol method)
    {
        method = GetDefinition(method);
        var members = method.ContainingType.GetMembers(method.Name);
        var candidates = new ISymbol[members.Length];
        var count = 0;
        foreach (var member in members)
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.Conversion })
            {
                candidates[count++] = member;
            }
        }

        if (count <= 1)
        {
            return -1;
        }

        Array.Sort(candidates, 0, count, DeclarationOrderComparer.Instance);
        var ordinal = candidates
            .AsStackEnumerable()
            .Take(count)
            .IndexOf(method, static (candidate, target) => candidate.IsEqualTo(target));
        if (ordinal >= 0)
        {
            return ordinal;
        }

        throw new InvalidOperationException(
            $"The method '{method.Name}' is not a member of '{method.ContainingType.Name}'.");
    }

    /// <summary>
    /// Returns the source hint name of the wrapper that the generator adds for a kernel class: the encoded path of the
    /// class, so that classes with the same name in different namespaces never share a hint name.
    /// </summary>
    public static string GetHintName(INamedTypeSymbol kernelClass) =>
        Shorten(string.Empty, GSharpNameEncoding.Encode(GetTypePath(kernelClass))) + ".g.cs";

    /// <summary>Returns <c>__gsharp_tp_{name}</c>.</summary>
    public static string GetTypeParameterName(ITypeParameterSymbol parameter) =>
        GSharpNameEncoding.TypeParameterPrefix + GSharpNameEncoding.EscapeName(parameter.Name);

    /// <summary>
    /// The C# path of <paramref name="type"/>: namespaces, containing types and the type, including concrete type
    /// arguments when <paramref name="type"/> (or an enclosing type) is a constructed generic type.
    /// </summary>
    public static GlobalNamePath GetTypePath(INamedTypeSymbol type) =>
        new(BuildTypeSegments(type));

    private static GlobalNamePath GetTypePath(ITypeSymbol type)
    {
        var modifiers = ImmutableArray.CreateBuilder<int>();
        var current = type;
        while (true)
        {
            if (current is IPointerTypeSymbol pointer)
            {
                modifiers.Add(0);
                current = pointer.PointedAtType;
            }
            else if (current is IArrayTypeSymbol array)
            {
                modifiers.Add(array.Rank);
                current = array.ElementType;
            }
            else
            {
                break;
            }
        }

        modifiers.Reverse();
        var segments = current is INamedTypeSymbol named
            ? BuildTypeSegments(named)
            : ImmutableArray.Create(new GlobalNameSegment(current.Name));
        return new GlobalNamePath(segments, modifiers.ToImmutable());
    }

    private static ImmutableArray<GlobalNameSegment> BuildTypeSegments(INamedTypeSymbol type)
    {
        var segments = ImmutableArray.CreateBuilder<GlobalNameSegment>();
        for (ISymbol? symbol = type; symbol is not null and not INamespaceSymbol { IsGlobalNamespace: true };
             symbol = symbol.ContainingSymbol)
        {
            if (symbol is INamedTypeSymbol named)
            {
                var definition = named.OriginalDefinition;
                var fileId = definition.IsFileLocal
                    ? GetFileOrdinal(definition).ToString("x", CultureInfo.InvariantCulture)
                    : null;
                if (IsConstructedSegment(named))
                {
                    var args = ImmutableArray.CreateBuilder<GlobalNamePath>(named.TypeArguments.Length);
                    foreach (var arg in named.TypeArguments)
                    {
                        args.Add(GetTypePath(arg));
                    }

                    segments.Add(new GlobalNameSegment(definition.Name, typeArguments: args.MoveToImmutable(),
                        arity: 0, fileId: fileId));
                }
                else
                {
                    segments.Add(new GlobalNameSegment(definition.Name, arity: definition.Arity, fileId: fileId));
                }
            }
            else
            {
                segments.Add(new GlobalNameSegment(symbol.Name));
            }
        }

        segments.Reverse();
        return segments.ToImmutable();
    }

    private static bool IsConstructedSegment(INamedTypeSymbol type)
    {
        if (type.Arity == 0 || type.IsDefinition || type.IsUnboundGenericType || type.TypeArguments.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < type.TypeArguments.Length; i++)
        {
            if (!type.TypeArguments[i].IsEqualTo(type.TypeParameters[i]))
            {
                return true;
            }
        }

        return false;
    }

    // [WHY] A file type is identified by its position among the file types with the same name and arity, ordered by
    // file path and then declaration position. Paths only order the candidates and never reach the name, so the name
    // does not depend on the machine; adding an unrelated file does not change it either.
    private static int GetFileOrdinal(INamedTypeSymbol type)
    {
        if (type.ContainingSymbol is not INamespaceOrTypeSymbol container)
        {
            return 0;
        }

        var members = container.GetTypeMembers(type.Name, type.Arity);
        var candidates = new INamedTypeSymbol[members.Length];
        var count = 0;
        foreach (var member in members)
        {
            if (member.IsFileLocal)
            {
                candidates[count++] = member;
            }
        }

        Array.Sort<INamedTypeSymbol>(candidates, 0, count, DeclarationOrderComparer.Instance);
        var ordinal = candidates
            .AsStackEnumerable()
            .Take(count)
            .IndexOf(type, static (candidate, target) => candidate.IsEqualTo(target));
        if (ordinal >= 0)
        {
            return ordinal;
        }

        throw new InvalidOperationException(
            $"The file type '{type.Name}' in '{DeclarationOrderComparer.GetFirstLocation(type).SourceTree?.FilePath}' is not a member of its containing symbol.");
    }

    // Orders declarations by file path, then by position in the file.
    private sealed class DeclarationOrderComparer : IComparer<ISymbol>
    {
        public static readonly DeclarationOrderComparer Instance = new();

        public int Compare(ISymbol? x, ISymbol? y)
        {
            var left = GetFirstLocation(x);
            var right = GetFirstLocation(y);
            var byPath = ComparePaths(left.SourceTree?.FilePath ?? string.Empty, right.SourceTree?.FilePath ?? string.Empty);
            return byPath != 0 ? byPath : left.SourceSpan.Start.CompareTo(right.SourceSpan.Start);
        }

        // Ordinal, with '\' read as '/' so that Windows and WSL builds order the same files the same way.
        private static int ComparePaths(string x, string y)
        {
            var length = Math.Min(x.Length, y.Length);
            for (var i = 0; i < length; i++)
            {
                var difference = Normalize(x[i]).CompareTo(Normalize(y[i]));
                if (difference != 0)
                {
                    return difference;
                }
            }

            return x.Length.CompareTo(y.Length);
        }

        private static char Normalize(char c) => c == '\\' ? '/' : c;

        public static Location GetFirstLocation(ISymbol? symbol) =>
            symbol is { Locations.Length: > 0 } ? symbol.Locations[0] : Location.None;
    }

    /// <summary>
    /// Returns <paramref name="prefix"/> + <paramref name="body"/>, or, when that exceeds <see cref="ShortenThreshold"/>,
    /// its head followed by <c>_h</c> and a 10-digit FNV-1a 64 hash of the whole name.
    /// </summary>
    /// <exception cref="InvalidOperationException">Two different names shorten to the same name.</exception>
    public static string Shorten(string prefix, string body)
    {
        var full = prefix + body;
        if (full.Length <= ShortenThreshold)
        {
            return full;
        }

        var headLength = ShortenThreshold - HashMarker.Length - HashLength;
        while (headLength > prefix.Length && full[headLength - 1] == '_')
        {
            headLength--;
        }

        var shortened = new StringBuilder(ShortenThreshold)
            .Append(full, 0, headLength)
            .Append(HashMarker)
            .Append(HashHelper.ComputeFnv1A64ShortHash(full))
            .ToString();

        var recorded = s_shortenedNames.GetOrAdd(shortened, full);
        if (!string.Equals(recorded, full, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The C++ names '{recorded}' and '{full}' both shorten to '{shortened}'.");
        }

        return shortened;
    }
}
