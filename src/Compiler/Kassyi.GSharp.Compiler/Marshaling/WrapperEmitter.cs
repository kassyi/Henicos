using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Generates host-side C# wrapper classes that load and invoke GPU kernels.</summary>
internal static class WrapperEmitter
{
    private const string EmptyArrayMessage = "Kernel array arguments must not be empty. Declare the parameter as nullable (T[]? or Memory<T>?) to accept null or empty input.";

    internal readonly struct InteropParameterInfo(
        string modifier,
        string? typeName,
        bool isIntPtr,
        string? defaultValue = null)
    {
        private string Modifier { get; } = modifier;
        private string? TypeName { get; } = typeName;
        private bool IsIntPtr { get; } = isIntPtr;
        private string? DefaultValue { get; } = defaultValue;

        public void WriteTo(SourceWriter writer, string name)
        {
            if (IsIntPtr)
            {
                writer.Append("IntPtr ");
            }
            else
            {
                if (!string.IsNullOrEmpty(Modifier))
                {
                    writer.Append(Modifier);
                    writer.Append(' ');
                }

                if (TypeName != null)
                {
                    writer.Append(TypeName);
                    writer.Append(' ');
                }
            }

            writer.Append(name);

            if (DefaultValue == null)
            {
                return;
            }

            writer.Append(" = ");
            writer.Append(DefaultValue);
        }
    }

    internal static InteropParameterInfo GetInteropParameterInfo(in KernelParameterShape shape, bool isDevicePtr)
    {
        switch (isDevicePtr)
        {
            case true when shape.IsDevicePointer:
                return new InteropParameterInfo(string.Empty, null, true, shape.HasExplicitDefault ? "default" : null);
            case true when shape.IsCustomStruct:
                return new InteropParameterInfo(string.Empty, shape.AotStructPath, false, shape.DefaultValueLiteral);
            default:
            {
                var modifier = shape.RefKind switch
                {
                    RefKind.Ref => "ref",
                    RefKind.Out => "out",
                    _ => string.Empty
                };

                return new InteropParameterInfo(modifier, shape.DeclaredTypeName, false, shape.DefaultValueLiteral);
            }
        }
    }

    // Generated lowered code (state machines, frames, and CPU/CUDA host wrappers) may contain synthesized labels,
    // unreachable branches after lowering, unused locals, unassigned internal frame/context fields, self-comparisons
    // (x != x for NaN), and unused local functions.
    private const string GeneratedCSharpWarningSuppressions =
        "#pragma warning disable CS0162, CS0164, CS0219, CS0649, CS1718, CS8321";

    // namespace <namespace> { <outer partial declarations> <accessibility> partial class <name>, without the namespace
    // for the global namespace. The generated part must declare the accessibility of the user's part, or the compiler
    // reports CS0262.
    private static void WriteClassDeclaration(SourceWriter writer, in HostClassHeader header)
    {
        if (!header.IsGlobalNamespace)
        {
            writer.AppendLine($"namespace {header.Namespace}");
            writer.AppendLine("{");
        }

        // A nested class is written inside partial declarations of the types that contain it
        foreach (var outer in header.OuterDeclarations)
        {
            writer.AppendLine(outer);
            writer.AppendLine("{");
        }

        writer.AppendLine($"{header.Accessibility} partial class {header.Name}");
    }

    private static void CloseClassDeclaration(SourceWriter writer, in HostClassHeader header)
    {
        for (var i = 0; i < header.OuterDeclarations.Length; i++)
        {
            writer.AppendLine("}");
        }

        writer.AppendLineIf(!header.IsGlobalNamespace, "}");
    }

    /// <summary>The assembly of StackLinq, whose namespace the generated managed code imports when it is referenced.</summary>
    public const string StackLinqAssemblyName = "Kassyi.ZeroAlloc.Linq";

    /// <summary>
    /// Emits the managed state machines and frames of the device functions in <paramref name="header"/>'s class that
    /// may suspend, as a part of that partial class.
    /// </summary>
    /// <param name="header">The class.</param>
    /// <param name="sources">The state machines.</param>
    /// <param name="referencesStackLinq">
    /// Whether the compilation references StackLinq; a library without kernels need not (its state machines are emitted
    /// for other assemblies, cross-assembly-device-code.md §3.7).
    /// </param>
    public static string GenerateDeviceStateMachines(in HostClassHeader header, IEnumerable<string> sources,
        bool referencesStackLinq)
    {
        // [WHY] The frames assign every field before the lowering reads it, which the nullable analysis cannot see
        // (CS8618, CS8601, CS8604 in every library that exports its device code); the annotations of the user's types
        // stay valid
        var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader(enableNullable: false);
        writer.AppendLine("#nullable enable annotations");
        writer.AppendLine("""
            using System;
            using global::Kassyi.GSharp.Core;
            using static global::Kassyi.GSharp.Core.ManagedGpuContext;
            """);
        writer.AppendLineIf(referencesStackLinq, "using global::Kassyi.ZeroAlloc.Linq;");
        writer.AppendLine(GeneratedCSharpWarningSuppressions);
        writer.AppendLine();

        WriteClassDeclaration(writer, header);
        using (writer.Scope())
        {
            foreach (var source in sources)
            {
                writer.AppendLine(source);
            }
        }

        CloseClassDeclaration(writer, header);
        return writer.ToString();
    }

    /// <summary>Emits the wrapper source of one kernel class.</summary>
    /// <param name="model">The kernels of the class.</param>
    /// <param name="config">The generator options.</param>
    /// <param name="assemblyName">The name of the assembly that declares the kernels.</param>
    public static string GenerateKernelExtensions(in KernelClassModel model, GeneratorConfig config, string assemblyName)
    {
        var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader(enableNullable: false);
        writer.AppendLine("#nullable enable annotations");
        writer.AppendLine("""
            using System;
            #pragma warning disable RS1035 // Do not use APIs banned for analyzers
            #pragma warning disable RS1016
            #pragma warning disable RS1034
            using System.Text;
            using System.Runtime.InteropServices;
            using global::Kassyi.GSharp.Core;
            using global::Kassyi.GSharp.Runtime.Interop;
            using static global::Kassyi.GSharp.Core.ManagedGpuContext;
            using global::Kassyi.ZeroAlloc.Linq;
            """);
        writer.AppendLine(GeneratedCSharpWarningSuppressions);

        writer.AppendLine();

        WriteClassDeclaration(writer, model.Class);
        using (writer.Scope())
        {
            writer.AppendGeneratedCodeAttributes();
            writer.AppendLine($"public static partial class {AotHostNames.AotContextsClassName}");
            using (writer.Scope())
            {
                StructMarshalingEmitter.WriteStructMarshaling(writer, model.Structs, config.IsCuda);
            }

            foreach (var kernel in model.Kernels)
            {
                // GSH0059: the analyzer reports the overload; its wrappers would duplicate an earlier overload's.
                if (kernel.IsIndistinguishable)
                {
                    continue;
                }

                WriteKernelSource(writer, kernel, config);

                if (config.IsCuda)
                {
                    CudaHostLaunchEmitter.WriteKernelMembers(writer, kernel, assemblyName);
                }
                else
                {
                    CpuHostLaunchEmitter.WriteKernelMembers(writer, kernel, assemblyName);
                }
            }
        }

        CloseClassDeclaration(writer, model.Class);
        return writer.ToString();
    }

    private static void WriteKernelSource(SourceWriter writer, in KernelEmission kernel, GeneratorConfig config)
    {
        if (!config.EmitSource)
        {
            return;
        }

        var key = kernel.MemberKey;
        writer.Append($"public const string {key}_Source = @\"");
        writer.AppendVerbatimLiteral(kernel.GetSource(config.IsCuda));
        writer.AppendLine("\";");
        writer.AppendLine(
            $"public static readonly string {key}_FullSource=" +
            $"global::Kassyi.GSharp.Generated.{config.SharedPreludeClassName}.Source +" +
            $"global::Kassyi.GSharp.Generated.{config.SharedDeviceCodeClassName}.Source +" +
            $"{key}_Source;");
        writer.AppendLine();
    }

    internal static void WriteLaunchMethodSignature(SourceWriter writer, in KernelEmission kernel, bool isDevicePtr)
    {
        writer.AppendGeneratedCodeAttributes();
        // An overloaded kernel's IntPtr wrapper is private: callers select the overload through the typed wrapper.
        writer.Append(isDevicePtr && kernel.IsOverloaded
            ? "private static unsafe "
            : "public static unsafe ");
        WriteLaunchReturnType(writer, kernel.Host);
        writer.Append(isDevicePtr
            ? KernelHostNames.GetPointerLaunchName(kernel)
            : KernelHostNames.GetLaunchName(kernel.Host));

        using (writer.Parentheses())
        {
            foreach (var p in kernel.Host.Parameters)
            {
                GetInteropParameterInfo(p, isDevicePtr).WriteTo(writer, p.Identifier);
                writer.Append(", ");
            }

            writer.Append(LaunchOptionParameters);
        }

        writer.AppendLine();
    }

    internal const string LaunchOptionParameters =
        "uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1," +
        "uint blockDimZ = 1, uint sharedMemBytes = 0, IntPtr hStream = default, " +
        "System.ReadOnlySpan<System.Collections.Generic.KeyValuePair<string, string>> macros = default";

    internal static void WriteLaunchReturnType(SourceWriter writer, in KernelHostModel kernel)
    {
        if (kernel.ReturnTypeName is { } returnType)
        {
            writer.Append($"{returnType}[] ");
        }
        else
        {
            writer.Append("void ");
        }
    }

    // The flag of a kernel whose launch has passed its layout guards once (WriteLayoutGuards)
    private const string LayoutCheckedPrefix = "s_layoutChecked_";

    // private static bool s_layoutChecked_<key>; for a kernel with layout guards
    internal static void WriteLayoutCheckedField(SourceWriter writer, EquatableArray<StructLayoutGuard> guards,
        string memberKey) =>
        writer.AppendLineIf(guards.Length > 0, $"private static bool {LayoutCheckedPrefix}{memberKey};");

    // if (!s_layoutChecked_<key>) {
    //   For each block-copied struct:
    //   if (Unsafe.SizeOf<S>() != <predicted host size>) LayoutGuard.ThrowSizeMismatch(...);
    //   { S __gsharp_layout_probe = default;
    //     if (Unsafe.ByteOffset(ref Unsafe.As<S, byte>(ref probe), ref Unsafe.As<F, byte>(ref Unsafe.AsRef(in probe.f)))
    //         != <predicted offset>) LayoutGuard.ThrowFieldOffsetMismatch(...); ... }
    //   s_layoutChecked_<key> = true; }
    // [WHY] The CLR layout of a type does not change within a process, so the checks run until one launch passes them;
    // a guard that throws leaves the flag unset. Two first launches that race only run the checks twice.
    internal static void WriteLayoutGuards(SourceWriter writer, EquatableArray<StructLayoutGuard> guards,
        string memberKey)
    {
        if (guards.Length == 0)
        {
            return;
        }

        const string Unsafe = "global::System.Runtime.CompilerServices.Unsafe";
        const string Probe = GSharpInternalNames.Launcher.LayoutProbe;
        writer.AppendLine($"if (!{LayoutCheckedPrefix}{memberKey})");
        using (writer.Scope())
        {
            foreach (var guard in guards)
            {
                writer.AppendLine(
                    $"if ({Unsafe}.SizeOf<{guard.FullyQualifiedTypeName}>() != {guard.ExpectedSize}) " +
                    $"global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowSizeMismatch(\"{guard.DisplayTypeName}\", {guard.ExpectedSize}, " +
                    $"{Unsafe}.SizeOf<{guard.FullyQualifiedTypeName}>());");
                if (guard.Fields.Length == 0)
                {
                    continue;
                }

                using (writer.Scope())
                {
                    writer.AppendLine($"{guard.FullyQualifiedTypeName} {Probe} = default;");
                    foreach (var field in guard.Fields)
                    {
                        var offset =
                            $"{Unsafe}.ByteOffset(ref {Unsafe}.As<{guard.FullyQualifiedTypeName}, byte>(ref {Probe}), " +
                            $"ref {Unsafe}.As<{field.FullyQualifiedTypeName}, byte>(ref {Unsafe}.AsRef(in {Probe}.{field.Name})))";
                        writer.AppendLine(
                            $"if ({offset} != {field.ExpectedOffset}) " +
                            $"global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch(\"{guard.DisplayTypeName}\", " +
                            $"\"{field.Name}\", {field.ExpectedOffset}, (long){offset});");
                    }
                }
            }

            writer.AppendLine($"{LayoutCheckedPrefix}{memberKey} = true;");
        }
    }

    internal static void WriteOutParameterDefaults(SourceWriter writer, in KernelHostModel kernel)
    {
        foreach (var p in kernel.Parameters)
        {
            if (p.RefKind is not RefKind.Out || p.IsArray)
            {
                continue;
            }

            writer.AppendLine($"{p.Identifier} = default;");
        }
    }

    internal static void WriteArrayArgumentChecks(SourceWriter writer, in KernelHostModel kernel)
    {
        foreach (var shape in kernel.Parameters)
        {
            if (!shape.IsArray)
            {
                continue;
            }

            var name = shape.Identifier;
            if (shape.IsOptional)
            {
                writer.Append("if ");
                using (writer.Parentheses())
                {
                    writer.Append($"{name} is {{ {shape.LengthMember}: 0 }}");
                }

                using (writer.Scope())
                {
                    writer.AppendLine($"{name} = null;");
                }
            }
            else if (shape.Container == ArrayContainerKind.Array)
            {
                writer.Append("if ");
                using (writer.Parentheses())
                {
                    writer.Append($"{name} is null");
                }

                using (writer.Scope())
                {
                    writer.AppendLine($"throw new global::System.ArgumentNullException(nameof({name}));");
                }

                WriteEmptyArrayCheck(writer, $"{name}.Length", name);
            }
            else if (shape.Container == ArrayContainerKind.ImmutableArray)
            {
                writer.Append("if ");
                using (writer.Parentheses())
                {
                    writer.Append($"{name}.IsDefault");
                }

                using (writer.Scope())
                {
                    writer.AppendLine($"throw new global::System.ArgumentNullException(nameof({name}));");
                }

                WriteEmptyArrayCheck(writer, $"{name}.Length", name);
            }
            else
            {
                WriteEmptyArrayCheck(writer, shape.LengthExpression, name);
            }
        }
    }

    // if (<length> == 0) { throw new ArgumentException(<message>, nameof(<name>)); }
    private static void WriteEmptyArrayCheck(SourceWriter writer, string length, string name)
    {
        writer.Append("if ");
        using (writer.Parentheses())
        {
            writer.Append($"{length} == 0");
        }

        using (writer.Scope())
        {
            writer.AppendLine($"throw new global::System.ArgumentException(\"{EmptyArrayMessage}\", nameof({name}));");
        }
    }
}
