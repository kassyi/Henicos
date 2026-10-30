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
            throw Schema(root.Line, "The document root must be a mapping containing 'namespace', 'className' and 'methods'.");
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

        if (definition.LibraryName is not null && definition.ModuleName is not null)
        {
            throw Schema(LineOf(mapping, "moduleName"),
                "'moduleName' cannot be combined with 'libraryName'; a definition binds either a host library or a device module.");
        }

        if (mapping.TryGetValue("libraryNames", out var libraryNamesNode))
        {
            if (definition.LibraryName is null)
            {
                throw Schema(libraryNamesNode.Line, "'libraryNames' requires 'libraryName'.");
            }

            definition.LibraryNames = ReadLibraryNames(libraryNamesNode);
        }

        if (mapping.TryGetValue("methods", out var methodsNode) && !IsNull(methodsNode))
        {
            if (methodsNode is not YamlSequence methods)
            {
                throw Schema(methodsNode.Line, "'methods' must be a sequence.");
            }

            var builder = ImmutableArray.CreateBuilder<ApiMethodDefinition>(methods.Items.Count);
            foreach (var item in methods.Items)
            {
                builder.Add(ReadMethod(item, definition.BindingKind));
            }

            definition.Methods = builder.MoveToImmutable();
        }

        if (mapping.TryGetValue("classes", out var classesNode) && !IsNull(classesNode))
        {
            definition.Classes = ReadClasses(classesNode, definition);
        }

        return definition;
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

    private static NativeLibraryNames ReadLibraryNames(YamlNode node)
    {
        if (node is not YamlMapping mapping)
        {
            throw Schema(node.Line, "'libraryNames' must be a mapping of platform ('windows', 'linux', 'osx') to candidate names.");
        }

        return new NativeLibraryNames
        {
            Windows = ReadStringList(mapping, "windows"),
            Linux = ReadStringList(mapping, "linux"),
            OSX = ReadStringList(mapping, "osx")
        };
    }

    private static ApiMethodDefinition ReadMethod(YamlNode node, NativeApiBindingKind bindingKind)
    {
        if (node is not YamlMapping mapping)
        {
            throw Schema(node.Line, "Each entry of 'methods' must be a mapping.");
        }

        var method = new ApiMethodDefinition
        {
            Name = RequireScalar(mapping, "name"),
            NativeName = OptionalScalar(mapping, "nativeName"),
            ClassName = OptionalScalar(mapping, "className"),
            EntryPoint = OptionalScalar(mapping, "entryPoint"),
            StringMarshalling = OptionalScalar(mapping, "stringMarshalling"),
            IsUnsafe = OptionalBoolean(mapping, "isUnsafe"),
            ManagedName = OptionalScalar(mapping, "managed"),
            Summary = OptionalScalar(mapping, "summary"),
            XmlDoc = OptionalScalar(mapping, "xmlDoc"),
            ReturnType = OptionalScalar(mapping, "returnType") ?? "void",
            ReturnDescription = OptionalScalar(mapping, "returnDescription"),
        };

        ValidateIdentifier(mapping, "name", method.Name);
        if (method.ClassName is not null && !IsValidDottedName(method.ClassName))
        {
            throw Schema(LineOf(mapping, "className"),
                $"'className' value '{method.ClassName}' is not a valid C# class name or nested class path.");
        }

        if (method.ManagedName is not null &&
            (bindingKind != NativeApiBindingKind.Intrinsic || !IsValidDottedName(method.ManagedName)))
        {
            throw Schema(LineOf(mapping, "managed"),
                $"'managed' value '{method.ManagedName}' must be a dotted member name of Kassyi.GSharp.Core on an intrinsic.");
        }

        if (method.StringMarshalling is not null)
        {
            ValidateIdentifier(mapping, "stringMarshalling", method.StringMarshalling);
        }

        if (bindingKind == NativeApiBindingKind.Module && method.ReturnType != "void")
        {
            throw Schema(LineOf(mapping, "returnType"),
                $"'returnType' must be 'void' for module (kernel) bindings, but method '{method.Name}' declares '{method.ReturnType}'.");
        }

        if (mapping.TryGetValue("typeTemplate", out var templateNode) && !IsNull(templateNode))
        {
            if (templateNode is not YamlMapping template)
            {
                throw Schema(templateNode.Line, "'typeTemplate' must be a mapping.");
            }

            method.TypeTemplate = new TypeTemplateDefinition
            {
                Placeholder = OptionalScalar(template, "placeholder") ?? "T",
                Types = ReadStringList(template, "types")
            };
        }

        if (mapping.TryGetValue("typeParameters", out var typeParametersNode) && !IsNull(typeParametersNode))
        {
            method.TypeParameters = ReadTypeParameters(typeParametersNode, method, bindingKind);
        }

        if (mapping.TryGetValue("parameters", out var parametersNode) && !IsNull(parametersNode))
        {
            if (parametersNode is not YamlSequence parameters)
            {
                throw Schema(parametersNode.Line, "'parameters' must be a sequence.");
            }

            var builder = ImmutableArray.CreateBuilder<ParameterDefinition>(parameters.Items.Count);
            foreach (var item in parameters.Items)
            {
                builder.Add(ReadParameter(item));
            }

            method.Parameters = builder.MoveToImmutable();
        }

        if (method.XmlDoc is not null && HasPlainTextDocumentation(method))
        {
            throw Schema(LineOf(mapping, "xmlDoc"),
                $"'xmlDoc' of method '{method.Name}' replaces 'summary', 'returnDescription' and the 'description' keys; write the whole documentation in 'xmlDoc'.");
        }

        return method;
    }

    private static bool HasPlainTextDocumentation(ApiMethodDefinition method)
    {
        if (method.Summary is not null || method.ReturnDescription is not null)
        {
            return true;
        }

        foreach (var parameter in method.Parameters)
        {
            if (parameter.Description is not null)
            {
                return true;
            }
        }

        foreach (var typeParameter in method.TypeParameters)
        {
            if (typeParameter.Description is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<TypeParameterDefinition> ReadTypeParameters(
        YamlNode node, ApiMethodDefinition method, NativeApiBindingKind bindingKind)
    {
        if (bindingKind != NativeApiBindingKind.Intrinsic)
        {
            throw Schema(node.Line,
                $"'typeParameters' is supported only by intrinsic definitions, but method '{method.Name}' binds a host library or a kernel module.");
        }

        if (node is not YamlSequence sequence)
        {
            throw Schema(node.Line, "'typeParameters' must be a sequence.");
        }

        var builder = ImmutableArray.CreateBuilder<TypeParameterDefinition>(sequence.Items.Count);
        foreach (var item in sequence.Items)
        {
            var typeParameter = ReadTypeParameter(item);
            if (typeParameter.Name == method.TypeTemplate?.Placeholder)
            {
                throw Schema(item.Line,
                    $"'typeParameters' entry '{typeParameter.Name}' of method '{method.Name}' is also the 'typeTemplate' placeholder.");
            }

            foreach (var previous in builder)
            {
                if (previous.Name == typeParameter.Name)
                {
                    throw Schema(item.Line,
                        $"'typeParameters' entry '{typeParameter.Name}' of method '{method.Name}' is declared twice.");
                }
            }

            builder.Add(typeParameter);
        }

        return builder.MoveToImmutable();
    }

    private static TypeParameterDefinition ReadTypeParameter(YamlNode node)
    {
        if (node is not YamlMapping mapping)
        {
            throw Schema(node.Line, "Each entry of 'typeParameters' must be a mapping.");
        }

        var typeParameter = new TypeParameterDefinition
        {
            Name = RequireScalar(mapping, "name"),
            Constraints = ReadStringList(mapping, "constraints"),
            Description = OptionalScalar(mapping, "description"),
        };

        ValidateIdentifier(mapping, "name", typeParameter.Name);
        return typeParameter;
    }

    private static ParameterDefinition ReadParameter(YamlNode node)
    {
        if (node is not YamlMapping mapping)
        {
            throw Schema(node.Line, "Each entry of 'parameters' must be a mapping.");
        }

        var parameter = new ParameterDefinition
        {
            Name = RequireScalar(mapping, "name"),
            Type = RequireScalar(mapping, "type"),
            Description = OptionalScalar(mapping, "description"),
            DefaultValue = OptionalScalar(mapping, "defaultValue"),
        };

        ValidateIdentifier(mapping, "name", parameter.Name);
        return parameter;
    }

    private static ImmutableArray<string> ReadStringList(YamlMapping mapping, string key)
    {
        if (!mapping.TryGetValue(key, out var node) || IsNull(node))
        {
            return ImmutableArray<string>.Empty;
        }

        if (node is YamlScalar single)
        {
            return ImmutableArray.Create(Normalize(single.Value));
        }

        if (node is not YamlSequence sequence)
        {
            throw Schema(node.Line, $"'{key}' must be a scalar or a sequence of scalars.");
        }

        var builder = ImmutableArray.CreateBuilder<string>(sequence.Items.Count);
        foreach (var item in sequence.Items)
        {
            if (item is not YamlScalar scalar || scalar.IsNull)
            {
                throw Schema(item.Line, $"Each entry of '{key}' must be a non-empty scalar.");
            }

            builder.Add(Normalize(scalar.Value));
        }

        return builder.MoveToImmutable();
    }

    private static string RequireScalar(YamlMapping mapping, string key)
    {
        var value = OptionalScalar(mapping, key);
        if (value is null || value.Length == 0)
        {
            throw Schema(mapping.Line, $"'{key}' is required.");
        }

        return value;
    }

    private static string? OptionalScalar(YamlMapping mapping, string key)
    {
        if (!mapping.TryGetValue(key, out var node) || IsNull(node))
        {
            return null;
        }

        return node is YamlScalar scalar
            ? Normalize(scalar.Value)
            : throw Schema(node.Line, $"'{key}' must be a scalar value.");
    }

    private static bool OptionalBoolean(YamlMapping mapping, string key)
    {
        var value = OptionalScalar(mapping, key);
        if (value is null)
        {
            return false;
        }

        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            ? false
            : throw Schema(LineOf(mapping, key), $"'{key}' must be 'true' or 'false'.");
    }

    private static void ValidateNamespace(YamlMapping mapping, string value)
    {
        if (!IsValidDottedName(value))
        {
            throw Schema(LineOf(mapping, "namespace"), $"'namespace' value '{value}' is not a valid C# namespace.");
        }
    }

    /// <summary>Whether every dot-separated segment of <paramref name="value"/> is a C# identifier.</summary>
    private static bool IsValidDottedName(string value)
    {
        var start = 0;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i < value.Length && value[i] != '.')
            {
                continue;
            }

            if (!SyntaxFacts.IsValidIdentifier(value[start..i]))
            {
                return false;
            }

            start = i + 1;
        }

        return true;
    }

    private static void ValidateIdentifier(YamlMapping mapping, string key, string value)
    {
        if (!SyntaxFacts.IsValidIdentifier(value))
        {
            throw Schema(LineOf(mapping, key), $"'{key}' value '{value}' is not a valid C# identifier.");
        }
    }

    private static int LineOf(YamlMapping mapping, string key) =>
        mapping.TryGetValue(key, out var node) ? node.Line : mapping.Line;

    private static bool IsNull(YamlNode node) => node is YamlScalar { IsNull: true };

    private static string Normalize(string value) => value.TrimEnd('\n', '\r');

    private static NativeApiReadException Schema(int line, string message) =>
        new(NativeApiReadErrorKind.Schema, line, message);
}
