namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Produces C# sources for <see cref="NativeApiBindingKind.Library"/> and <see cref="NativeApiBindingKind.Module"/>
/// definitions. Used by the <c>NativeApiGenerator</c> source generator, which adds the output to the consuming
/// compilation. Free of I/O.
/// </summary>
internal static class NativeBindingEmitter
{
    public const string ResolverClassName = "GSharpNativeLibraryResolver";

    /// <summary>
    /// Emits self-contained <c>extern</c> <c>[DllImport]</c> P/Invoke bindings for a
    /// <see cref="NativeApiBindingKind.Library"/> definition. <c>[LibraryImport]</c> is not an option because the
    /// <c>LibraryImport</c> generator never sees other generators' output, so its <c>partial</c> declarations would
    /// stay unimplemented.
    /// </summary>
    public static string EmitLibraryBindings(NativeApiDefinition def)
    {
        using var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader();
        writer.AppendLine("using System;");
        writer.AppendLine("using System.Runtime.InteropServices;");
        writer.AppendLine();
        WriteNamespace(writer, def.Namespace);
        writer.AppendLine("// ReSharper disable InconsistentNaming");
        writer.AppendExcludeFromCodeCoverageAttribute();
        WriteClassDeclaration(writer, def.ClassName);
        using (writer.Scope())
        {
            foreach (var m in def.Methods)
            {
                EmitLibraryMethod(writer, def.LibraryName ?? string.Empty, m);
            }
        }

        return writer.ToString();
    }

    // [DllImport("<library>", EntryPoint = "<entry point>", ExactSpelling = true[, CharSet = CharSet.Unicode])]
    // public static [unsafe ]extern <return type> <name>(<parameters>);
    private static void EmitLibraryMethod(SourceWriter writer, string libraryName, ApiMethodDefinition m)
    {
        NativeApiDocumentation.EmitDocumentation(writer, m);
        writer.Append($"[DllImport({ModuleKernelLaunchEmitter.Literal(libraryName)}, ");
        writer.Append($"EntryPoint = {ModuleKernelLaunchEmitter.Literal(GetEntryPoint(m))}, ExactSpelling = true");
        writer.AppendIf(m.StringMarshalling == "Utf16", ", CharSet = CharSet.Unicode");
        writer.AppendLine(")]");

        writer.Append($"public static {(m.IsUnsafe ? "unsafe " : "")}extern {m.ReturnType} ");
        writer.AppendCsharpIdentifier(m.Name);
        using (writer.Parentheses())
        {
            var isUtf8 = m.StringMarshalling == "Utf8";
            for (var i = 0; i < m.Parameters.Length; i++)
            {
                var p = m.Parameters[i];
                writer.AppendIf(i > 0, ", ");
                writer.AppendIf(isUtf8 && p.Type is "string" or "string?", "[MarshalAs(UnmanagedType.LPUTF8Str)] ");
                writer.Append($"{p.Type} {p.Name.ToCSharpIdentifier()}");
            }
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    /// <summary>
    /// Emits host launchers for kernels contained in a <see cref="NativeApiBindingKind.Module"/> definition.
    /// </summary>
    public static string EmitModuleBindings(NativeApiDefinition def)
    {
        var moduleName = def.ModuleName ?? string.Empty;

        using var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader();
        writer.AppendLine("using System;");
        writer.AppendLine("using System.Runtime.InteropServices;");
        writer.AppendLine("using Kassyi.GSharp.Runtime;");
        writer.AppendLine("using Kassyi.GSharp.Runtime.Aot;");
        writer.AppendLine("using Kassyi.GSharp.Runtime.Interop;");
        writer.AppendLine();
        WriteNamespace(writer, def.Namespace);
        writer.AppendExcludeFromCodeCoverageAttribute();
        WriteClassDeclaration(writer, def.ClassName);
        using (writer.Scope())
        {
            ModuleKernelLaunchEmitter.EmitModuleCacheField(writer, moduleName);

            foreach (var m in def.Methods)
            {
                var plan = ModuleBindingPlanner.Plan(m);

                NativeApiDocumentation.EmitDocumentation(writer, m);
                writer.Append(plan.TakesPointer ? "public static unsafe void " : "public static void ");
                writer.AppendCsharpIdentifier(m.Name);
                WriteModuleParameterList(writer, m);
                writer.AppendLine();
                using (writer.Scope())
                {
                    ModuleKernelLaunchEmitter.EmitLaunchBody(writer, moduleName, GetEntryPoint(m), m.Name,
                        $"{def.ClassName}.{m.Name}", plan, packContext: false);
                }

                writer.AppendLine();
            }
        }

        return writer.ToString();
    }

    // (<type> <name>[ = <default>], ...) as the YAML definition writes them
    private static void WriteModuleParameterList(SourceWriter writer, ApiMethodDefinition method)
    {
        using (writer.Parentheses())
        {
            for (var i = 0; i < method.Parameters.Length; i++)
            {
                var parameter = method.Parameters[i];
                writer.AppendIf(i > 0, ", ");
                writer.Append($"{parameter.Type} {parameter.Name.ToCSharpIdentifier()}");
                if (!string.IsNullOrEmpty(parameter.DefaultValue))
                {
                    writer.Append($" = {parameter.DefaultValue}");
                }
            }
        }
    }

    // namespace <segment>.<segment>; with each segment that is a keyword escaped (My.@class)
    private static void WriteNamespace(SourceWriter writer, string ns)
    {
        writer.Append("namespace ");
        writer.AppendCsharpNamespace(ns);
        writer.AppendLine(";");
        writer.AppendLine();
    }

    // public static partial class <name>
    private static void WriteClassDeclaration(SourceWriter writer, string className)
    {
        writer.Append("public static partial class ");
        writer.AppendCsharpTypeIdentifier(className);
        writer.AppendLine();
    }

    // The native symbol of a method: its entryPoint, or its name
    private static string GetEntryPoint(ApiMethodDefinition method) =>
        method.EntryPoint is { Length: > 0 } entryPoint ? entryPoint : method.Name;

    /// <summary>
    /// Emits a single per-assembly <c>DllImportResolver</c> that probes, for each library name, the candidates that
    /// <see cref="NativeLibraryResolverPlanner"/> merged from every definition of that name.
    /// </summary>
    public static string EmitLibraryResolver(ImmutableArray<NativeLibraryCandidates> libraries)
    {
        using var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader();
        writer.Append("#pragma warning disable CA2255 ");
        writer.AppendLine("// ModuleInitializer is required to register the resolver before first P/Invoke");
        writer.AppendLine("using System;");
        writer.AppendLine("using System.Reflection;");
        writer.AppendLine("using System.Runtime.CompilerServices;");
        writer.AppendLine("using System.Runtime.InteropServices;");
        writer.AppendLine();
        writer.AppendLine("namespace Kassyi.GSharp.Generated;");
        writer.AppendLine();
        writer.AppendExcludeFromCodeCoverageAttribute();
        writer.AppendLine($"internal static class {ResolverClassName}");
        using (writer.Scope())
        {
            for (var i = 0; i < libraries.Length; i++)
            {
                EmitCandidateField(writer, i, "Windows", libraries[i].Windows);
                EmitCandidateField(writer, i, "Linux", libraries[i].Linux);
                EmitCandidateField(writer, i, "OSX", libraries[i].OSX);
            }

            writer.AppendLine();
            writer.AppendLine("[ModuleInitializer]");
            writer.AppendLine("internal static void Register() =>");
            writer.AppendLine($"NativeLibrary.SetDllImportResolver(typeof({ResolverClassName}).Assembly, Resolve);");
            writer.AppendLine();
            EmitResolveMethod(writer, libraries);
        }

        return writer.ToString();
    }

    // private static IntPtr Resolve(...) { switch (libraryName) { case "<name>": return <probe the candidates>; }
    // return IntPtr.Zero; }
    private static void EmitResolveMethod(SourceWriter writer, ImmutableArray<NativeLibraryCandidates> libraries)
    {
        writer.Append("private static IntPtr Resolve(string libraryName, Assembly assembly, ");
        writer.AppendLine("DllImportSearchPath? searchPath)");
        using (writer.Scope())
        {
            writer.AppendLine("switch (libraryName)");
            using (writer.Scope())
            {
                for (var i = 0; i < libraries.Length; i++)
                {
                    writer.AppendLine($"case {ModuleKernelLaunchEmitter.Literal(libraries[i].LibraryName)}:");
                    writer.Append("return global::Kassyi.GSharp.Runtime.Interop.NativeLibraryProbe.Load");
                    writer.AppendLine("(assembly, searchPath,");
                    writer.Append($"OperatingSystem.IsWindows() ? s_candidates{i}Windows : ");
                    writer.AppendLine($"OperatingSystem.IsLinux() ? s_candidates{i}Linux : s_candidates{i}OSX);");
                }
            }

            writer.AppendLine();
            writer.AppendLine("return IntPtr.Zero;");
        }
    }

    // private static readonly string[] s_candidates<index><platform> = ["<candidate>", ...];
    private static void EmitCandidateField(SourceWriter writer, int index, string platform,
        EquatableArray<string> candidates)
    {
        writer.Append($"private static readonly string[] s_candidates{index}{platform} = [");
        for (var i = 0; i < candidates.Length; i++)
        {
            writer.AppendIf(i > 0, ", ");
            writer.Append(ModuleKernelLaunchEmitter.Literal(candidates[i]));
        }

        writer.AppendLine("];");
    }
}
