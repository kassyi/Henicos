namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Produces GPU-only intrinsic stubs from <see cref="NativeApiBindingKind.Intrinsic"/> definitions. Used by the
/// build-time <c>native-api</c> tool, which writes the stubs into <c>Kassyi.GSharp.Core</c>. Free of I/O.
/// </summary>
internal static class NativeIntrinsicEmitter
{
    /// <summary>
    /// Merges the intrinsic definitions that declare the same class (namespace and <c>className</c>) into one, in the
    /// given order, so that several YAML files can add members to one class (<c>Gpu</c>) and its nested classes.
    /// Library and module definitions are dropped.
    /// </summary>
    public static ImmutableArray<NativeApiDefinition> MergeByClass(ImmutableArray<NativeApiDefinition> definitions)
    {
        var merged = ImmutableArray.CreateBuilder<NativeApiDefinition>();
        foreach (var def in definitions)
        {
            if (def.BindingKind != NativeApiBindingKind.Intrinsic)
            {
                continue;
            }

            var target = FindClass(merged, def);
            if (target is null)
            {
                merged.Add(new NativeApiDefinition
                {
                    Namespace = def.Namespace,
                    ClassName = def.ClassName,
                    ClassNameLine = def.ClassNameLine,
                    Methods = def.Methods,
                    Classes = def.Classes,
                });
                continue;
            }

            target.Methods = target.Methods.AddRange(def.Methods);
            target.Classes = target.Classes.AddRange(def.Classes);
        }

        return merged.ToImmutable();
    }

    private static NativeApiDefinition? FindClass(ImmutableArray<NativeApiDefinition>.Builder merged, NativeApiDefinition def)
    {
        foreach (var candidate in merged)
        {
            if (candidate.Namespace == def.Namespace && candidate.ClassName == def.ClassName)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Emits GPU-only intrinsic stubs.
    /// </summary>
    public static string EmitIntrinsics(NativeApiDefinition def)
    {
        using var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader();
        writer.Line("using System;");
        writer.Line();
        writer.Line($"namespace {def.Namespace};");
        writer.Line();

        // Group by target class while preserving first-appearance order.
        var classNames = ImmutableArray.CreateBuilder<string>();
        foreach (var m in def.Methods)
        {
            var className = string.IsNullOrEmpty(m.ClassName) ? def.ClassName : m.ClassName ?? def.ClassName;
            if (!classNames.Contains(className))
            {
                classNames.Add(className);
            }
        }

        foreach (var className in classNames)
        {
            EmitClass(writer, def, className, 0);
            writer.Line();
        }

        return writer.ToString();
    }

    /// <summary>
    /// Emits the class at <paramref name="classPath"/> from <paramref name="segmentStart"/> on, wrapping a nested class
    /// (<c>Gpu.Unsafe</c>) in <c>partial</c> declarations of its outer classes.
    /// </summary>
    private static void EmitClass(SourceWriter writer, NativeApiDefinition def, string classPath, int segmentStart)
    {
        var dot = classPath.IndexOf('.', segmentStart);
        if (dot >= 0)
        {
            // [WHY] The outer class has its own declaration; repeating the attribute here would duplicate it (CS0579).
            writer.Line($"public static partial class {classPath[segmentStart..dot]}");
            using (writer.Scope())
            {
                EmitClass(writer, def, classPath, dot + 1);
            }

            return;
        }

        foreach (var documented in def.Classes)
        {
            if (documented.Name == classPath)
            {
                NativeApiDocumentation.EmitXmlDoc(writer, documented.XmlDoc);
            }
        }

        writer.AppendExcludeFromCodeCoverageAttribute();
        writer.Line($"public static partial class {classPath[segmentStart..]}");
        using (writer.Scope())
        {
            writer.Line("#pragma warning disable IDE0051 // Remove unused private members");
            writer.Line("// ReSharper disable UnusedMember.Global");
            writer.Line();

            foreach (var m in def.Methods)
            {
                var methodClass = string.IsNullOrEmpty(m.ClassName) ? def.ClassName : m.ClassName ?? def.ClassName;
                if (methodClass != classPath)
                {
                    continue;
                }

                EmitIntrinsicOverloads(writer, m);
            }

            writer.Line("// ReSharper restore UnusedMember.Global");
            writer.Line("#pragma warning restore IDE0051");
        }
    }

    private static void EmitIntrinsicOverloads(SourceWriter writer, ApiMethodDefinition m)
    {
        var types = m.TypeTemplate?.Types ?? ImmutableArray<string>.Empty;
        if (types.IsEmpty)
        {
            types = ImmutableArray.Create(string.Empty);
        }

        // [WHY] Without a typeTemplate there is nothing to expand; replacing a default "T" would mangle the type names.
        var placeholder = m.TypeTemplate?.Placeholder ?? string.Empty;

        foreach (var t in types)
        {
            var returnType = ReplacePlaceholder(m.ReturnType, placeholder, t);

            NativeApiDocumentation.EmitDocumentation(writer, m);

            var nativeName = m.NativeName ?? m.Name;
            writer.Line($"[Kassyi.GSharp.Core.CudaIntrinsic(\"{nativeName}\")]");

            writer.Append("public static ");
            writer.AppendIf(m.IsUnsafe, "unsafe ");
            writer.Append($"{returnType} {m.Name}");
            AppendTypeParameterList(writer, m);
            using (writer.Parentheses())
            {
                for (var i = 0; i < m.Parameters.Length; i++)
                {
                    var p = m.Parameters[i];
                    if (i > 0)
                    {
                        writer.Append(", ");
                    }

                    var pType = ReplacePlaceholder(p.Type, placeholder, t);
                    writer.Append($"{pType} {p.Name}");
                    if (!string.IsNullOrEmpty(p.DefaultValue))
                    {
                        writer.Append($" = {p.DefaultValue}");
                    }
                }
            }

            AppendConstraintClauses(writer, m, placeholder, t);
            if (m.ManagedName is { } managedName)
            {
                AppendForwardingBody(writer, m, managedName);
            }
            else
            {
                AppendThrowingBody(writer, returnType);
            }

            writer.Line();
        }
    }

    //  => Kassyi.GSharp.Core.<managed member>(<ref|in|out> <parameter>, ...);
    // The managed member runs the intrinsic on the managed CPU path and throws outside a managed launch itself
    private static void AppendForwardingBody(SourceWriter writer, ApiMethodDefinition m, string managedName)
    {
        writer.Append($" => Kassyi.GSharp.Core.{managedName}");
        using (writer.Parentheses())
        {
            for (var i = 0; i < m.Parameters.Length; i++)
            {
                var p = m.Parameters[i];
                writer.AppendIf(i > 0, ", ");
                writer.Append(ArgumentModifier(p.Type));
                writer.Append(p.Name);
            }
        }

        writer.AppendLine(";");
    }

    // The modifier an argument of a parameter of type <type> is passed with: ref T -> "ref ", ref readonly T and in T ->
    // "in ", out T -> "out "
    private static string ArgumentModifier(string type)
    {
        if (type.StartsWith("ref readonly ", StringComparison.Ordinal) || type.StartsWith("in ", StringComparison.Ordinal))
        {
            return "in ";
        }

        if (type.StartsWith("ref ", StringComparison.Ordinal))
        {
            return "ref ";
        }

        return type.StartsWith("out ", StringComparison.Ordinal) ? "out " : string.Empty;
    }

    //  => GpuOnlyException.Throw();  |  => GpuOnlyException.Throw<<return type>>();
    //  => [(<pointer type>)]GpuOnlyException.Throw<nint>().ToPointer();
    private static void AppendThrowingBody(SourceWriter writer, string returnType)
    {
        if (returnType == "void")
        {
            writer.AppendLine(" => Kassyi.GSharp.Core.GpuOnlyException.Throw();");
            return;
        }

        // [WHY] A pointer type cannot be a type argument, so a pointer is made from a thrown nint.
        if (returnType.EndsWith("*", StringComparison.Ordinal))
        {
            writer.Append(" => ");
            writer.AppendIf(returnType != "void*", $"({returnType})");
            writer.AppendLine("Kassyi.GSharp.Core.GpuOnlyException.Throw<nint>().ToPointer();");
            return;
        }

        writer.AppendLine($" => Kassyi.GSharp.Core.GpuOnlyException.Throw<{returnType}>();");
    }

    // <T1, T2>
    private static void AppendTypeParameterList(SourceWriter writer, ApiMethodDefinition m)
    {
        if (m.TypeParameters.IsEmpty)
        {
            return;
        }

        using (writer.InlineScope("<", ">"))
        {
            for (var i = 0; i < m.TypeParameters.Length; i++)
            {
                writer.AppendIf(i > 0, ", ");
                writer.Append(m.TypeParameters[i].Name);
            }
        }
    }

    // where T1 : <constraint>, <constraint> where T2 : <constraint>
    private static void AppendConstraintClauses(SourceWriter writer, ApiMethodDefinition m, string placeholder, string type)
    {
        foreach (var typeParameter in m.TypeParameters)
        {
            for (var i = 0; i < typeParameter.Constraints.Length; i++)
            {
                writer.AppendIf(i == 0, $" where {typeParameter.Name} : ");
                writer.AppendIf(i > 0, ", ");
                writer.Append(ReplacePlaceholder(typeParameter.Constraints[i], placeholder, type));
            }
        }
    }

    /// <summary>
    /// Replaces each whole-identifier occurrence of <paramref name="placeholder"/>, so that <c>T</c> becomes the
    /// element type in <c>ref T</c> and <c>Fragment&lt;TShape, T&gt;</c> but leaves <c>TShape</c> alone.
    /// </summary>
    private static string ReplacePlaceholder(string template, string placeholder, string replacement)
    {
        if (string.IsNullOrEmpty(placeholder) || template.IndexOf(placeholder, StringComparison.Ordinal) < 0)
        {
            return template;
        }

        var sb = new StringBuilder(template.Length + replacement.Length);
        var copied = 0;
        var index = template.IndexOf(placeholder, StringComparison.Ordinal);
        while (index >= 0)
        {
            var end = index + placeholder.Length;
            var isWholeIdentifier =
                (index == 0 || !SyntaxFacts.IsIdentifierPartCharacter(template[index - 1])) &&
                (end == template.Length || !SyntaxFacts.IsIdentifierPartCharacter(template[end]));
            if (isWholeIdentifier)
            {
                sb.Append(template, copied, index - copied).Append(replacement);
                copied = end;
            }

            index = template.IndexOf(placeholder, end, StringComparison.Ordinal);
        }

        return sb.Append(template, copied, template.Length - copied).ToString();
    }
}
