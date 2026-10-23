namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Produces GPU-only intrinsic stubs from <see cref="NativeApiBindingKind.Intrinsic"/> definitions. Used by the
/// build-time <c>native-api</c> tool, which writes the stubs into <c>Kassyi.GSharp.Core</c>. Free of I/O.
/// </summary>
internal static class NativeIntrinsicEmitter
{
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

            writer.Append($"public static {returnType} {m.Name}");
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
            writer.AppendLine(returnType == "void"
                ? " => Kassyi.GSharp.Core.GpuOnlyException.Throw();"
                : $" => Kassyi.GSharp.Core.GpuOnlyException.Throw<{returnType}>();");
            writer.Line();
        }
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
