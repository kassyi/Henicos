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
                NativeApiDocumentation.EmitDocumentation(writer, m);

                var isUtf8 = m.StringMarshalling == "Utf8";
                writer.Append($"[DllImport({Literal(def.LibraryName ?? string.Empty)}, ");
                writer.Append($"EntryPoint = {Literal(GetEntryPoint(m))}, ExactSpelling = true");
                if (m.StringMarshalling == "Utf16")
                {
                    writer.Append(", CharSet = CharSet.Unicode");
                }

                writer.AppendLine(")]");

                var unsafeMod = m.IsUnsafe ? "unsafe " : "";
                writer.Append($"public static {unsafeMod}extern {m.ReturnType} {m.Name.ToCSharpIdentifier()}(");
                for (var i = 0; i < m.Parameters.Length; i++)
                {
                    var p = m.Parameters[i];
                    if (i > 0)
                    {
                        writer.Append(", ");
                    }

                    if (isUtf8 && p.Type is "string" or "string?")
                    {
                        writer.Append("[MarshalAs(UnmanagedType.LPUTF8Str)] ");
                    }

                    writer.Append($"{p.Type} {p.Name.ToCSharpIdentifier()}");
                }

                writer.AppendLine(");");
                writer.AppendLine();
            }
        }

        return writer.ToString();
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
                writer.Append(TakesPointer(plan) ? "public static unsafe void " : "public static void ");
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
        var start = 0;
        for (var i = 0; i <= ns.Length; i++)
        {
            if (i < ns.Length && ns[i] != '.')
            {
                continue;
            }

            writer.AppendIf(start > 0, ".");
            writer.AppendCsharpIdentifier(ns[start..i]);
            start = i + 1;
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    // public static partial class <name>
    private static void WriteClassDeclaration(SourceWriter writer, string className)
    {
        writer.Append("public static partial class ");
        writer.AppendCsharpIdentifier(className);
        writer.AppendLine();
    }

    // The native symbol of a method: its entryPoint, or its name
    private static string GetEntryPoint(ApiMethodDefinition method) =>
        string.IsNullOrEmpty(method.EntryPoint) ? method.Name : method.EntryPoint ?? method.Name;

    // A pointer or function pointer parameter needs an unsafe launcher (CS0214 otherwise)
    private static bool TakesPointer(ModuleKernelLaunchPlan plan) =>
        plan.Arguments
            .AsStackEnumerable()
            .Any(static a => a.Kind == ModuleKernelParameterKind.Pointer);

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>
    /// Returns <see langword="true"/> when the library of <paramref name="def"/> cannot be found by the default
    /// probing logic: it declares platform candidates or is given as a relative or absolute path.
    /// </summary>
    public static bool RequiresResolver(NativeApiDefinition def)
    {
        if (def.BindingKind != NativeApiBindingKind.Library)
        {
            return false;
        }

        if (def.LibraryNames is { IsEmpty: false })
        {
            return true;
        }

        var libraryName = def.LibraryName ?? string.Empty;
        return libraryName.IndexOf('/') >= 0 || libraryName.IndexOf('\\') >= 0;
    }

    /// <summary>
    /// Emits a single per-assembly <c>DllImportResolver</c> that probes, for each library name, the candidates that
    /// <see cref="NativeLibraryResolverPlanner"/> merged from every definition of that name.
    /// </summary>
    public static string EmitLibraryResolver(ImmutableArray<NativeLibraryCandidates> libraries)
    {
        using var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader();
        writer.AppendLine("#pragma warning disable CA2255 // ModuleInitializer is required to register the resolver before first P/Invoke");
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
            writer.AppendLine("private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)");
            using (writer.Scope())
            {
                writer.AppendLine("switch (libraryName)");
                using (writer.Scope())
                {
                    for (var i = 0; i < libraries.Length; i++)
                    {
                        writer.AppendLine($"case {Literal(libraries[i].LibraryName)}:");
                        writer.AppendLine("return global::Kassyi.GSharp.Runtime.Interop.NativeLibraryProbe.Load(assembly, searchPath,");
                        writer.Append($"OperatingSystem.IsWindows() ? s_candidates{i}Windows : ");
                        writer.AppendLine($"OperatingSystem.IsLinux() ? s_candidates{i}Linux : s_candidates{i}OSX);");
                    }
                }

                writer.AppendLine();
                writer.AppendLine("return IntPtr.Zero;");
            }
        }

        return writer.ToString();
    }

    // private static readonly string[] s_candidates<index><platform> = ["<candidate>", ...];
    private static void EmitCandidateField(SourceWriter writer, int index, string platform,
        EquatableArray<string> candidates)
    {
        writer.Append($"private static readonly string[] s_candidates{index}{platform} = [");
        for (var i = 0; i < candidates.Length; i++)
        {
            writer.AppendIf(i > 0, ", ");
            writer.Append(Literal(candidates[i]));
        }

        writer.AppendLine("];");
    }
}
