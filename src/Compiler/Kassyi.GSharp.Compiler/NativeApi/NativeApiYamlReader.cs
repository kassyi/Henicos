using static Kassyi.GSharp.Compiler.NativeApi.NativeApiYamlValues;

namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Maps a YAML native API definition onto <see cref="NativeApiDefinition"/> and validates its schema.
/// </summary>
/// <remarks>
/// <para>Unknown keys are ignored so that definitions stay forward-compatible. String values that end up in
/// generated code are validated as C# identifiers where they name a symbol; free-form values such as
/// types and descriptions are passed through and escaped by the emitter.</para>
/// <para>A parameter or return <c>type</c> is the C# spelling, modifiers included (<c>ref T</c>, <c>in T</c>,
/// <c>out T</c>, <c>ref readonly T</c>, <c>WmmaMatrixA&lt;TShape, T, TLayout&gt;</c>). Intrinsic definitions (neither
/// <c>libraryName</c> nor <c>moduleName</c>) can also declare a generic method and a nested class:</para>
/// <code>
/// - name: CopyAsync
///   nativeName: __gsharp_cp_async
///   className: Gpu.Unsafe          # dotted path: a static class nested in Gpu
///   typeParameters:
///     - name: T
///       constraints: unmanaged     # a scalar or a sequence, emitted in order after "where T :"
///       description: The element type.
///   parameters:
///     - { name: sharedDst, type: ref T }
///     - { name: globalSrc, type: ref readonly T }
/// </code>
/// <para>Documentation that needs XML markup is written whole in <c>xmlDoc</c> (a method key, which replaces the
/// escaped <c>summary</c> and <c>description</c> keys) and, for a nested class, in the top-level <c>classes</c> list.
/// <c>isUnsafe: true</c> declares an intrinsic <c>unsafe</c>; one that returns a pointer throws through
/// <c>GpuOnlyException.Throw&lt;nint&gt;().ToPointer()</c>.</para>
/// <para><c>managed: ManagedAtomics.Add</c> names the member of <c>Kassyi.GSharp.Core</c> that runs an intrinsic on the
/// managed CPU path: the stub forwards its arguments to it (<c>=&gt; Kassyi.GSharp.Core.ManagedAtomics.Add(ref address, val);</c>)
/// instead of throwing.</para>
/// <code>
/// classes:
///   - name: Gpu.Unsafe
///     xmlDoc: |
///       &lt;summary&gt;Provides low-level unsafe device memory management intrinsics.&lt;/summary&gt;
/// methods:
///   - name: Free
///     nativeName: free
///     className: Gpu.Unsafe
///     isUnsafe: true
///     xmlDoc: |
///       &lt;summary&gt;Frees unmanaged memory allocated by &lt;see cref="Malloc"/&gt; (&lt;c&gt;free&lt;/c&gt;).&lt;/summary&gt;
///       &lt;param name="ptr"&gt;A raw pointer to the allocated memory block to free.&lt;/param&gt;
///     parameters:
///       - { name: ptr, type: void* }
/// </code>
/// </remarks>
internal static class NativeApiYamlReader
{
    public static bool TryRead(
        string text,
        [NotNullWhen(true)] out NativeApiDefinition? definition,
        [NotNullWhen(false)] out NativeApiReadError? error)
    {
        try
        {
            definition = Read(text);
            error = null;
            return true;
        }
        catch (NativeApiReadException ex)
        {
            definition = null;
            error = ex.ToError();
            return false;
        }
    }

    private static NativeApiDefinition Read(string text)
    {
        var root = YamlSubsetParser.Parse(text);
        if (root is not YamlMapping mapping)
        {
            throw Schema(root.Line,
                "The document root must be a mapping containing 'namespace', 'className' and 'methods'.");
        }

        var definition = new NativeApiDefinition
        {
            Namespace = RequireScalar(mapping, "namespace"),
            ClassName = RequireScalar(mapping, "className"),
            LibraryName = OptionalScalar(mapping, "libraryName"),
            ModuleName = OptionalScalar(mapping, "moduleName"),
        };

        ValidateNamespace(mapping, definition.Namespace);
        ValidateIdentifier(mapping, "className", definition.ClassName);
        definition.ClassNameLine = LineOf(mapping, "className");
        ReadBindingTarget(mapping, definition);

        if (mapping.TryGetValue("methods", out var methodsNode) && !IsNull(methodsNode))
        {
            definition.Methods = ReadSequence(methodsNode, "methods", definition.BindingKind, NativeApiMethodReader.ReadMethod);
        }

        if (mapping.TryGetValue("classes", out var classesNode) && !IsNull(classesNode))
        {
            definition.Classes = ReadClasses(classesNode, definition);
        }

        return definition;
    }

    // A definition binds a host library (libraryName, optionally libraryNames per platform) or a device module
    private static void ReadBindingTarget(YamlMapping mapping, NativeApiDefinition definition)
    {
        if (definition.LibraryName is not null && definition.ModuleName is not null)
        {
            throw Schema(LineOf(mapping, "moduleName"),
                "'moduleName' cannot be combined with 'libraryName'; " +
                "a definition binds either a host library or a device module.");
        }

        if (mapping.TryGetValue("libraryNames", out var libraryNamesNode))
        {
            if (definition.LibraryName is null)
            {
                throw Schema(libraryNamesNode.Line, "'libraryNames' requires 'libraryName'.");
            }

            definition.LibraryNames = ReadLibraryNames(libraryNamesNode);
        }
    }

    private static ImmutableArray<ClassDocumentationDefinition> ReadClasses(YamlNode node, NativeApiDefinition definition)
    {
        if (definition.BindingKind != NativeApiBindingKind.Intrinsic)
        {
            throw Schema(node.Line, "'classes' is supported only by intrinsic definitions.");
        }

        if (node is not YamlSequence sequence)
        {
            throw Schema(node.Line, "'classes' must be a sequence.");
        }

        var builder = ImmutableArray.CreateBuilder<ClassDocumentationDefinition>(sequence.Items.Count);
        foreach (var item in sequence.Items)
        {
            if (item is not YamlMapping mapping)
            {
                throw Schema(item.Line, "Each entry of 'classes' must be a mapping.");
            }

            var entry = new ClassDocumentationDefinition
            {
                Name = RequireScalar(mapping, "name"),
                XmlDoc = RequireScalar(mapping, "xmlDoc"),
            };

            // [WHY] The documentation goes on the declaration that holds the class's methods; any other entry is lost.
            if (!DeclaresMethods(definition, entry.Name))
            {
                throw Schema(LineOf(mapping, "name"),
                    $"'classes' entry '{entry.Name}' names no class that a method is declared in.");
            }

            foreach (var previous in builder)
            {
                if (previous.Name == entry.Name)
                {
                    throw Schema(LineOf(mapping, "name"), $"'classes' entry '{entry.Name}' is declared twice.");
                }
            }

            builder.Add(entry);
        }

        return builder.MoveToImmutable();
    }

    private static bool DeclaresMethods(NativeApiDefinition definition, string className)
    {
        foreach (var method in definition.Methods)
        {
            var methodClass = string.IsNullOrEmpty(method.ClassName) ? definition.ClassName : method.ClassName;
            if (methodClass == className)
            {
                return true;
            }
        }

        return false;
    }

    private static NativeLibraryNames ReadLibraryNames(YamlNode node) =>
        node is YamlMapping mapping
            ? new NativeLibraryNames
            {
                Windows = ReadStringList(mapping, "windows"),
                Linux = ReadStringList(mapping, "linux"),
                OSX = ReadStringList(mapping, "osx")
            }
            : throw Schema(node.Line,
                "'libraryNames' must be a mapping of platform ('windows', 'linux', 'osx') to candidate names.");

    private static void ValidateNamespace(YamlMapping mapping, string value)
    {
        if (!IsValidDottedName(value))
        {
            throw Schema(LineOf(mapping, "namespace"), $"'namespace' value '{value}' is not a valid C# namespace.");
        }
    }
}
