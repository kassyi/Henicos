using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Generates host-side C# wrapper classes that load and invoke GPU kernels.</summary>
internal static class WrapperEmitter
{
    private const string NegativeLengthMessage = "The array length must not be negative.";
    private const string InconsistentProxyMessage = "A null array has no elements, so its length must be 0.";

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
                writer.Append("global::System.IntPtr ");
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
        switch (isDevicePtr ? PointerLaunchPlan.Classify(shape) : PointerArgumentKind.Value)
        {
            case PointerArgumentKind.Array:
                return new InteropParameterInfo(
                    string.Empty,
                    HostTypeNames.ArrayProxy,
                    false,
                    shape.HasExplicitDefault ? "default" : null
                );
            case PointerArgumentKind.NullableArray:
                return new InteropParameterInfo(
                    string.Empty,
                    HostTypeNames.ArrayProxy + "?",
                    false,
                    shape.HasExplicitDefault ? "default" : null
                );
            case PointerArgumentKind.Reference:
                return new InteropParameterInfo(string.Empty, null, true, shape.HasExplicitDefault ? "default" : null);
            case PointerArgumentKind.ShadowStruct:
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

    // CS0162 = Lowered state machines emit statements or 'goto case' transitions immediately after unconditional throw or goto
    // CS0164 = Lowered state machines preserve user labels (e.g. sync:) or synthesized branch targets (__gsharp_sg_next_<n>) that no jump targets
    // CS0219 = Lowered state machines declare locals (e.g. int unused = 0) that are assigned but never read
    // CS0649 = Internal frame fields are populated by the runtime rather than C# assignment
    // CS1718 = Lowered switch/pattern dispatch performs __gsharp_switch_temp_<n> != __gsharp_switch_temp_<n> self-comparisons for NaN detection
    private const string GeneratedCSharpWarningSuppressions =
        "#pragma warning disable CS0162, CS0164, CS0219, CS0649, CS1718";

    // namespace <namespace> { <outer partial declarations> <accessibility> partial <keyword> <name>, without the
    // namespace for the global namespace. The generated part must declare the accessibility of the user's part, or the
    // compiler reports CS0262, and its kind (class, record), or CS0261.
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

        writer.AppendLine($"{header.Accessibility} partial {header.Keyword} {header.Name}");
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
        WriteDefinedSymbols(writer, header);
        // #nullable enable annotations = Preserves user type annotations without emitting warnings for unassigned generated fields
        writer.AppendLine("#nullable enable annotations");
        writer.AppendLine($"""
            using System;
            using global::Kassyi.GSharp.Core;
            using static {ManagedNames.Context};
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

    // #define <symbol> for each symbol that the user's files of the class define and the kept [Conditional] calls
    // of the copied bodies need; before the first token of the file, as C# requires (CS1032)
    private static void WriteDefinedSymbols(SourceWriter writer, in HostClassHeader header)
    {
        foreach (var symbol in header.DefinedSymbols)
        {
            writer.AppendLine($"#define {symbol}");
        }
    }

    /// <summary>Emits the wrapper source of one kernel class.</summary>
    /// <param name="model">The kernels of the class.</param>
    /// <param name="config">The generator options.</param>
    /// <param name="moduleName">The module name of the assembly that declares the kernels.</param>
    public static string GenerateKernelExtensions(in KernelClassModel model, GeneratorConfig config, string moduleName)
    {
        var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader(enableNullable: false);
        WriteDefinedSymbols(writer, model.Class);
        // #nullable enable annotations = Preserves user type annotations without emitting warnings for unassigned generated fields
        writer.AppendLine("#nullable enable annotations");
        writer.AppendLine($"""
            using System;
            #pragma warning disable RS1035 // Do not use APIs banned for analyzers
            #pragma warning disable RS1016
            #pragma warning disable RS1034
            using System.Text;
            using System.Runtime.InteropServices;
            using global::Kassyi.GSharp.Core;
            using global::Kassyi.GSharp.Runtime.Interop;
            using static {ManagedNames.Context};
            using global::Kassyi.ZeroAlloc.Linq;
            """);
        writer.AppendLine(GeneratedCSharpWarningSuppressions);

        writer.AppendLine();

        WriteClassDeclaration(writer, model.Class);
        using (writer.Scope())
        {
            WriteErrorSiteTableField(writer, model.ErrorSites);
            writer.AppendGeneratedCodeAttributes();
            writer.AppendLine($"public static partial class {AotHostNames.AotContextsClassName}");
            using (writer.Scope())
            {
                StructMarshalingEmitter.WriteStructMarshaling(writer, model.Structs, config.IsCuda);
            }

            foreach (var kernel in model.Kernels)
            {
                // GSH0059 / GSH0106: the analyzer reports the overload or reserved launch parameter name; its wrappers would not compile.
                if (kernel.IsIndistinguishable || kernel.Host.HasReservedLaunchParameterName)
                {
                    continue;
                }

                // GSH0108: the analyzer reports the field type the launcher cannot name; a launcher that throws keeps
                // the callers compiling, so that the build shows GSH0108 rather than errors in generated code
                if (kernel.Host.LauncherUnavailableReason is { } reason)
                {
                    WriteUnavailableLaunchMethod(writer, kernel, reason);
                    continue;
                }

                WriteKernelSource(writer, kernel, config);

                if (config.IsCuda)
                {
                    CudaHostLaunchEmitter.WriteKernelMembers(writer, kernel, moduleName, config.IsCheckedBuild);
                }
                else
                {
                    CpuHostLaunchEmitter.WriteKernelMembers(writer, kernel, moduleName);
                }
            }
        }

        CloseClassDeclaration(writer, model.Class);
        return writer.ToString();
    }

    // public static unsafe <return> Launch<Name>(<parameters>, <launch options>)
    // { throw new NotSupportedException("<reason>"); }
    private static void WriteUnavailableLaunchMethod(SourceWriter writer, in KernelEmission kernel, string reason)
    {
        WriteLaunchMethodSignature(writer, kernel, isDevicePtr: false);
        using (writer.Scope())
        {
            writer.Append("throw new global::System.NotSupportedException(");
            NativeApi.ModuleKernelLaunchEmitter.AppendStringLiteral(writer, reason);
            writer.AppendLine(");");
        }
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

    internal static string LaunchOptionParameters => KernelHostNames.LaunchOptionParameters;

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

    // private static bool __gsharp_layout_checked_<key>; for a kernel with layout guards
    internal static void WriteLayoutCheckedField(SourceWriter writer, EquatableArray<StructLayoutGuard> guards,
        string memberKey) =>
        writer.AppendLineIf(guards.Length > 0, $"private static bool {KernelHostNames.GetLayoutCheckedFieldName(memberKey)};");

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

        const string Unsafe = HostTypeNames.Unsafe;
        const string Probe = GSharpInternalNames.Launcher.LayoutProbe;
        var layoutChecked = KernelHostNames.GetLayoutCheckedFieldName(memberKey);
        writer.AppendLine($"if (!{layoutChecked})");
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

            writer.AppendLine($"{layoutChecked} = true;");
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

    /// <summary>
    /// Emits the checks of the array arguments of the <c>Launch{Kernel}</c> launcher. An array-like argument is passed as
    /// a C# call passes it, a null, empty or default one included; only a null in a non-nullable array field of a
    /// by-value struct argument is rejected.
    /// </summary>
    internal static void WriteArrayArgumentChecks(SourceWriter writer, in KernelHostModel kernel) =>
        WriteStructFieldChecks(writer, kernel, isPointer: false);

    // if (<name>.<field path> is null)
    // { throw new ArgumentNullException("<name>", "<name>.<field path> is null, ..."); }
    // if (<name>.<field path>.Data == IntPtr.Zero) { ... }   (the shadow struct of the pointer launcher)
    // [WHY] The kernel reads a non-nullable array field of a by-value struct argument without a null check
    // (NonNullProof); the launcher enforces the annotation of the field (#1409)
    private static void WriteStructFieldChecks(SourceWriter writer, in KernelHostModel kernel, bool isPointer)
    {
        foreach (var shape in kernel.Parameters)
        {
            if (shape.NonNullableArrayFields.IsDefaultOrEmpty)
            {
                continue;
            }

            var name = shape.Identifier;
            foreach (var path in shape.NonNullableArrayFields)
            {
                writer.Append("if ");
                using (writer.Parentheses())
                {
                    writer.Append(isPointer
                        ? $"{name}.{path}.Data == {HostTypeNames.IntPtr}.Zero"
                        : $"{name}.{path} is null");
                }

                using (writer.Scope())
                {
                    var message = Literal($"{shape.Name}.{path} is null, but the field is not nullable.");
                    writer.AppendLine(
                        $"throw new {HostTypeNames.ArgumentNullException}({Literal(shape.Name)}, {message});");
                }
            }
        }
    }

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>
    /// Emits the checks of the <c>GSharpArrayProxy</c> arguments of the <c>Launch{Kernel}Ptr</c> launcher: a proxy with
    /// a negative length, or with no data and a length other than 0, describes no array and is rejected. Any other
    /// proxy is passed as it is: <c>Data == 0</c> is a null array, and an empty array has a non-null <c>Data</c>
    /// (<c>GSharpArrayProxy.EmptyArrayData</c>). The non-nullable array fields of a by-value struct argument must not
    /// be null.
    /// </summary>
    internal static void WritePointerArrayArgumentChecks(SourceWriter writer, in KernelHostModel kernel)
    {
        foreach (var shape in kernel.Parameters)
        {
            if (!PointerLaunchPlan.IsProxy(PointerLaunchPlan.Classify(shape)))
            {
                continue;
            }

            var proxy = PointerLaunchPlan.GetProxyExpression(shape);
            writer.Append("if ");
            using (writer.Parentheses())
            {
                writer.Append($"{proxy}.Length < 0");
            }

            using (writer.Scope())
            {
                writer.Append($"throw new {HostTypeNames.ArgumentOutOfRangeException}({Literal(shape.Name)}, ");
                writer.AppendLine($"{proxy}.Length, \"{NegativeLengthMessage}\");");
            }

            writer.Append("if ");
            using (writer.Parentheses())
            {
                writer.Append($"{proxy}.Data == {HostTypeNames.IntPtr}.Zero && {proxy}.Length != 0");
            }

            using (writer.Scope())
            {
                writer.Append($"throw new {HostTypeNames.ArgumentException}(\"{InconsistentProxyMessage}\", ");
                writer.AppendLine($"{Literal(shape.Name)});");
            }
        }

        WriteStructFieldChecks(writer, kernel, isPointer: true);
    }

    private static void WriteErrorSiteTableField(SourceWriter writer, EquatableArray<ErrorSiteEntry> errorSites)
    {
        if (errorSites.IsDefaultOrEmpty)
        {
            
            return;
        }

        writer.AppendLine("private static readonly global::Kassyi.GSharp.Runtime.Aot.GSharpErrorSite[] s_errorSites = new global::Kassyi.GSharp.Runtime.Aot.GSharpErrorSite[]");
        writer.AppendLine("{");
        foreach (var site in errorSites)
        {
            var fileLit = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(site.File, quote: true);
            var memberLit = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(site.MemberName, quote: true);
            var msgLit = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(site.DefaultMessage, quote: true);
            writer.AppendLine($"new global::Kassyi.GSharp.Runtime.Aot.GSharpErrorSite({site.SiteId}u, {fileLit}, {site.Line}, {site.Column}, {memberLit}, {msgLit}),");
        }
        writer.AppendLine("};");
        writer.AppendLine();
    }
}