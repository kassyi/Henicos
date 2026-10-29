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
        writer.AppendLine($"namespace {def.Namespace};");
        writer.AppendLine();
        writer.AppendLine("// ReSharper disable InconsistentNaming");
        writer.AppendExcludeFromCodeCoverageAttribute();
        writer.AppendLine($"public static partial class {def.ClassName}");
        using (writer.Scope())
        {
            foreach (var m in def.Methods)
            {
                NativeApiDocumentation.EmitDocumentation(writer, m);

                var isUtf8 = m.StringMarshalling == "Utf8";
                writer.Append("[DllImport(");
                ModuleKernelLaunchEmitter.AppendStringLiteral(writer, def.LibraryName ?? string.Empty);
                writer.Append(", EntryPoint = ");
                ModuleKernelLaunchEmitter.AppendStringLiteral(writer,
                    string.IsNullOrEmpty(m.EntryPoint) ? m.Name : m.EntryPoint ?? m.Name);
                writer.Append(", ExactSpelling = true");
                if (m.StringMarshalling == "Utf16")
                {
                    writer.Append(", CharSet = CharSet.Unicode");
                }

                writer.AppendLine(")]");

                var unsafeMod = m.IsUnsafe ? "unsafe " : "";
                writer.Append($"public static {unsafeMod}extern {m.ReturnType} {m.Name}(");
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

                    writer.Append($"{p.Type} {p.Name}");
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
        writer.AppendLine($"namespace {def.Namespace};");
        writer.AppendLine();
        writer.AppendExcludeFromCodeCoverageAttribute();
        writer.AppendLine($"public static partial class {def.ClassName}");
        using (writer.Scope())
        {
            ModuleKernelLaunchEmitter.EmitModuleCacheField(writer, moduleName);

            foreach (var m in def.Methods)
            {
                var parameters = CreateLaunchParameters(m);

                NativeApiDocumentation.EmitDocumentation(writer, m);
                writer.Append($"public static void {m.Name}(");
                for (var i = 0; i < m.Parameters.Length; i++)
                {
                    var p = m.Parameters[i];
                    if (i > 0)
                    {
                        writer.Append(", ");
                    }

                    writer.Append($"{p.Type} {p.Name}");
                    if (!string.IsNullOrEmpty(p.DefaultValue))
                    {
                        writer.Append($" = {p.DefaultValue}");
                    }
                }

                writer.AppendLine(")");
                using (writer.Scope())
                {
                    var entryPoint = string.IsNullOrEmpty(m.EntryPoint) ? m.Name : m.EntryPoint ?? m.Name;
                    ModuleKernelLaunchEmitter.EmitLaunchBody(writer, moduleName, entryPoint, m.Name, $"{def.ClassName}.{m.Name}",
                        parameters, packContext: false);
                }

                writer.AppendLine();
            }
        }

        return writer.ToString();
    }

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
    /// Emits a single per-assembly <c>DllImportResolver</c> that probes the candidates declared by every
    /// definition for which <see cref="RequiresResolver"/> is <see langword="true"/>.
    /// </summary>
    public static string EmitLibraryResolver(ImmutableArray<NativeApiDefinition> definitions)
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
            var index = 0;
            foreach (var def in definitions)
            {
                EmitCandidateField(writer, index, "Windows", def.LibraryNames?.Windows, def.LibraryName);
                EmitCandidateField(writer, index, "Linux", def.LibraryNames?.Linux, def.LibraryName);
                EmitCandidateField(writer, index, "OSX", def.LibraryNames?.OSX, def.LibraryName);
                index++;
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
                    index = 0;
                    foreach (var def in definitions)
                    {
                        writer.Append("case ");
                        ModuleKernelLaunchEmitter.AppendStringLiteral(writer, def.LibraryName ?? string.Empty);
                        writer.AppendLine(":");
                        writer.AppendLine("return global::Kassyi.GSharp.Runtime.Interop.NativeLibraryProbe.Load(assembly, searchPath,");
                        writer.AppendLine($"OperatingSystem.IsWindows() ? s_candidates{index}Windows : OperatingSystem.IsLinux() ? s_candidates{index}Linux : s_candidates{index}OSX);");
                        index++;
                    }
                }

                writer.AppendLine();
                writer.AppendLine("return IntPtr.Zero;");
            }
        }

        return writer.ToString();
    }

    private static void EmitCandidateField(SourceWriter writer, int index, string platform,
        ImmutableArray<string>? candidates, string? libraryName)
    {
        writer.Append($"private static readonly string[] s_candidates{index}{platform} = [");
        var first = true;
        if (candidates is { } list)
        {
            foreach (var candidate in list)
            {
                if (!first)
                {
                    writer.Append(", ");
                }

                ModuleKernelLaunchEmitter.AppendStringLiteral(writer, candidate);
                first = false;
            }
        }

        // The logical name is always the last resort so that a plain OS search still applies.
        if (!first)
        {
            writer.Append(", ");
        }

        ModuleKernelLaunchEmitter.AppendStringLiteral(writer, libraryName ?? string.Empty);
        writer.AppendLine("];");
    }

    private static ImmutableArray<ModuleKernelLaunchParameter> CreateLaunchParameters(ApiMethodDefinition method)
    {
        var builder = ImmutableArray.CreateBuilder<ModuleKernelLaunchParameter>(method.Parameters.Length);
        method.Parameters
            .AsStackEnumerable()
            .Select(CreateLaunchParameter)
            .AddTo(builder);

        return builder.MoveToImmutable();
    }

    private static ModuleKernelLaunchParameter CreateLaunchParameter(ParameterDefinition p)
    {
        var type = p.Type.AsSpan().Trim().ToString();
        string? elementType = null;
        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            elementType = type.AsSpan(0, type.Length - 2).TrimEnd().ToString();
        }

        return new ModuleKernelLaunchParameter(p.Name, type, elementType, CopyIn: true, CopyOut: true);
    }
}
