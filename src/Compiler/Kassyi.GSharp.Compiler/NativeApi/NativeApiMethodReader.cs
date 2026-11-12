using static Kassyi.GSharp.Compiler.NativeApi.NativeApiYamlValues;

namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Reads an entry of the <c>methods</c> list of a YAML native API definition: its keys, type parameters and
/// parameters, validated against the binding kind of the definition.
/// </summary>
internal static class NativeApiMethodReader
{
    internal static ApiMethodDefinition ReadMethod(YamlNode node, NativeApiBindingKind bindingKind)
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
            UnsupportedDialects = ReadStringList(mapping, "unsupported"),
            Summary = OptionalScalar(mapping, "summary"),
            XmlDoc = OptionalScalar(mapping, "xmlDoc"),
            ReturnType = OptionalScalar(mapping, "returnType") ?? "void",
            ReturnDescription = OptionalScalar(mapping, "returnDescription"),
        };

        ValidateMethod(mapping, method, bindingKind);
        ReadMethodComponents(mapping, method, bindingKind);
        return method;
    }

    private static void ValidateMethod(
        YamlMapping mapping, ApiMethodDefinition method, NativeApiBindingKind bindingKind)
    {
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
                $"'managed' value '{method.ManagedName}' must be a dotted member name " +
                "of Kassyi.GSharp.Core on an intrinsic.");
        }

        ValidateUnsupportedDialects(mapping, method, bindingKind);
        if (method.StringMarshalling is not null)
        {
            ValidateIdentifier(mapping, "stringMarshalling", method.StringMarshalling);
        }

        if (bindingKind == NativeApiBindingKind.Module && method.ReturnType != "void")
        {
            throw Schema(LineOf(mapping, "returnType"),
                $"'returnType' must be 'void' for module (kernel) bindings, but method '{method.Name}' " +
                $"declares '{method.ReturnType}'.");
        }
    }

    // unsupported: [Cpu]: only on an intrinsic, only the dialects that exist, and not together with the managed member
    // that would implement it on the CPU dialect
    private static void ValidateUnsupportedDialects(
        YamlMapping mapping, ApiMethodDefinition method, NativeApiBindingKind bindingKind)
    {
        if (method.UnsupportedDialects.IsEmpty)
        {
            return;
        }

        var line = LineOf(mapping, "unsupported");
        if (bindingKind != NativeApiBindingKind.Intrinsic)
        {
            throw Schema(line, $"'unsupported' of method '{method.Name}' is supported only by intrinsic definitions.");
        }

        const string Cpu = ApiMethodDefinition.CpuDialect;
        const string Cuda = ApiMethodDefinition.CudaDialect;
        foreach (var dialect in method.UnsupportedDialects)
        {
            if (dialect is not (Cpu or Cuda))
            {
                throw Schema(line,
                    $"'unsupported' value '{dialect}' of method '{method.Name}' must be '{Cpu}' or '{Cuda}'.");
            }

            if (dialect == Cpu && method.ManagedName is not null)
            {
                throw Schema(line,
                    $"'unsupported' of method '{method.Name}' names '{Cpu}', whose managed path runs the 'managed' " +
                    $"member '{method.ManagedName}'; remove one of the two.");
            }
        }
    }

    private static void ReadMethodComponents(
        YamlMapping mapping, ApiMethodDefinition method, NativeApiBindingKind bindingKind)
    {
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
            method.Parameters = ReadSequence(parametersNode, "parameters", (Kind: bindingKind, Method: method.Name),
                static (item, state) => ReadParameter(item, state.Kind, state.Method));
        }

        if (method.XmlDoc is not null && HasPlainTextDocumentation(method))
        {
            throw Schema(LineOf(mapping, "xmlDoc"),
                $"'xmlDoc' of method '{method.Name}' replaces 'summary', 'returnDescription' and " +
                "the 'description' keys; write the whole documentation in 'xmlDoc'.");
        }
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
                "'typeParameters' is supported only by intrinsic definitions, " +
                $"but method '{method.Name}' binds a host library or a kernel module.");
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
                    $"'typeParameters' entry '{typeParameter.Name}' of method '{method.Name}' " +
                    "is also the 'typeTemplate' placeholder.");
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

    private static ParameterDefinition ReadParameter(YamlNode node, NativeApiBindingKind bindingKind, string methodName)
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
        if (bindingKind == NativeApiBindingKind.Module)
        {
            var type = parameter.Type.AsSpan().Trim().ToString();
            if (ModuleKernelParameterTypes.Parse(parameter.Name, type).Reason is { } reason)
            {
                throw Schema(LineOf(mapping, "type"),
                    $"Parameter '{parameter.Name}' of kernel method '{methodName}' has type '{type}', " +
                    $"which a module launcher cannot pass: {reason}");
            }
        }

        return parameter;
    }
}
