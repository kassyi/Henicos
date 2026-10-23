using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Generates host-side C# wrapper classes that load and invoke GPU kernels.</summary>
internal static class WrapperEmitter
{
    private const string TotalThreads = "__gsharp_total_threads";

    private const string EmptyArrayMessage = "Kernel array arguments must not be empty. Declare the parameter as nullable (T[]? or Memory<T>?) to accept null or empty input.";

    internal readonly struct InteropParameterInfo(
        string modifier,
        ITypeSymbol? type,
        bool isIntPtr,
        string? defaultValue = null,
        string? rawTypeName = null)
    {
        private string Modifier { get; } = modifier;
        private ITypeSymbol? Type { get; } = type;
        private bool IsIntPtr { get; } = isIntPtr;
        private string? DefaultValue { get; } = defaultValue;
        private string? RawTypeName { get; } = rawTypeName;

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

                if (RawTypeName != null)
                {
                    writer.Append(RawTypeName);
                    writer.Append(' ');
                }
                else if (Type != null)
                {
                    writer.AppendFullyQualifiedCsharpName(Type);
                    if (Type.IsReferenceType && Type.NullableAnnotation == NullableAnnotation.Annotated)
                    {
                        writer.Append("?");
                    }
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

    internal static InteropParameterInfo GetInteropParameterInfo(IParameterSymbol p, bool isDevicePtr)
    {
        string? defaultValue = null;
        if (p.HasExplicitDefaultValue)
        {
            var val = p.ExplicitDefaultValue;
            defaultValue = val switch
            {
                null => "default",
                bool b => b ? "true" : "false",
                float f => f.ToString(CultureInfo.InvariantCulture) + "f",
                double d => d.ToString(CultureInfo.InvariantCulture) + "d",
                string s => SymbolDisplay.FormatLiteral(s, true),
                _ => val.ToString()
            };
        }

        var shape = KernelParameterShape.Classify(p);
        switch (isDevicePtr)
        {
            case true when shape.IsDevicePointer:
            {
                if (p.HasExplicitDefaultValue)
                {
                    defaultValue = "default";
                }

                return new InteropParameterInfo(string.Empty, null, true, defaultValue);
            }
            case true when shape.IsCustomStruct:
            {
                var tempWriter = new SourceWriter();
                tempWriter.AppendAotStructPath(shape.MarshaledType);
                var rawTypeName = tempWriter.ToString();
                return new InteropParameterInfo(string.Empty, null, false, defaultValue, rawTypeName);
            }
            default:
            {
                var modifier = p.RefKind switch
                {
                    RefKind.Ref => "ref",
                    RefKind.Out => "out",
                    _ => string.Empty
                };

                return new InteropParameterInfo(modifier, p.Type, false, defaultValue);
            }
        }
    }

    // The generated part must declare the accessibility of the user's part, or the compiler reports CS0262.
    private static string GetClassAccessibility(INamedTypeSymbol classSymbol) =>
        SyntaxFacts.GetText(classSymbol.DeclaredAccessibility);

    // A context or frame field that a kernel never assigns is reported (CS0649) unless the field is visible outside the
    // assembly, which it is only in a public class.
    private static void WriteUnassignedFieldSuppression(SourceWriter writer, INamedTypeSymbol classSymbol) =>
        writer.AppendLineIf(classSymbol.DeclaredAccessibility != Accessibility.Public, "#pragma warning disable CS0649");

    /// <summary>
    /// Emits the managed state machines and frames of the device functions in <paramref name="classSymbol"/> that may
    /// suspend, as a part of that partial class.
    /// </summary>
    public static string GenerateDeviceStateMachines(INamedTypeSymbol classSymbol, IEnumerable<string> sources)
    {
        var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader();
        writer.AppendLine("""
            using System;
            using global::Kassyi.GSharp.Core;
            using static global::Kassyi.GSharp.Core.ManagedGpuContext;
            using global::Kassyi.ZeroAlloc.Linq;
            """);
        WriteUnassignedFieldSuppression(writer, classSymbol);
        writer.AppendLine();

        var isGlobalNamespace = classSymbol.ContainingNamespace.IsGlobalNamespace;
        if (!isGlobalNamespace)
        {
            writer.Append("namespace ");
            writer.AppendNamespace(classSymbol.ContainingNamespace);
            writer.AppendLine();
            writer.AppendLine("{");
        }

        writer.AppendLine($"{GetClassAccessibility(classSymbol)} partial class {classSymbol.Name}");
        using (writer.Scope())
        {
            foreach (var source in sources)
            {
                writer.AppendLine(source);
            }
        }

        if (!isGlobalNamespace)
        {
            writer.AppendLine("}");
        }

        return writer.ToString();
    }

    public static string GenerateKernelExtensions(INamedTypeSymbol classSymbol,
        IEnumerable<(IMethodSymbol Method, string Source, string ManagedCSharpSource)> methods, GeneratorConfig config)
    {
        var className = classSymbol.Name;

        var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader();
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
        WriteUnassignedFieldSuppression(writer, classSymbol);

        writer.AppendLine();

        var isGlobalNamespace = classSymbol.ContainingNamespace.IsGlobalNamespace;
        if (!isGlobalNamespace)
        {
            writer.Append("namespace ");
            writer.AppendNamespace(classSymbol.ContainingNamespace);
            writer.AppendLine();
            writer.AppendLine("{");
        }

        writer.AppendLine($"{GetClassAccessibility(classSymbol)} partial class {className}");
        using (writer.Scope())
        {
            writer.AppendGeneratedCodeAttributes();
            writer.AppendLine($"public static partial class {AotHostNames.AotContextsClassName}");
            using (writer.Scope())
            {
                // ReSharper disable once PossibleMultipleEnumeration
                StructMarshalingEmitter.WriteStructMarshaling(writer, methods);
            }

            // ReSharper disable once PossibleMultipleEnumeration
            foreach (var (method, source, managedCSharpSource) in methods)
            {
                // GSH0059: the generator reports the overload; its wrappers would duplicate an earlier overload's.
                if (KernelHostNames.TryFindIndistinguishableOverload(method, out _, out _))
                {
                    continue;
                }

                WriteKernelSource(writer, method, source, config);

                if (config.IsCuda)
                {
                    CudaHostLaunchEmitter.WriteKernelMembers(writer, method);
                }
                else
                {
                    CpuHostLaunchEmitter.WriteKernelMembers(writer, method, managedCSharpSource);
                }
            }
        }

        if (!isGlobalNamespace)
        {
            writer.AppendLine("}");
        }

        return writer.ToString();
    }

    private static void WriteKernelSource(SourceWriter writer, IMethodSymbol method, string source,
        GeneratorConfig config)
    {
        if (!config.EmitSource)
        {
            return;
        }

        var key = KernelHostNames.GetMemberKey(method);
        writer.Append($"public const string {key}_Source = @\"");
        writer.AppendVerbatimLiteral(source);
        writer.AppendLine("\";");
        writer.AppendLine(
            $"public static readonly string {key}_FullSource=" +
            $"global::Kassyi.GSharp.Generated.{config.SharedPreludeClassName}.Source +" +
            $"global::Kassyi.GSharp.Generated.{config.SharedDeviceCodeClassName}.Source +" +
            $"{key}_Source;");
        writer.AppendLine();
    }

    internal static void WriteLaunchMethodSignature(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        writer.AppendGeneratedCodeAttributes();
        // An overloaded kernel's IntPtr wrapper is private: callers select the overload through the typed wrapper.
        writer.Append(isDevicePtr && KernelHostNames.IsOverloadedKernel(method)
            ? "private static unsafe "
            : "public static unsafe ");
        WriteLaunchReturnType(writer, method);

        if (isDevicePtr)
        {
            writer.Append(KernelHostNames.GetPointerLaunchName(method));
        }
        else
        {
            writer.Append(KernelHostNames.GetLaunchName(method));
        }

        using (writer.Parentheses())
        {
            var isFirst = true;
            foreach (var p in method.Parameters)
            {
                if (!isFirst)
                {
                    writer.Append(", ");
                }

                var info = GetInteropParameterInfo(p, isDevicePtr);
                info.WriteTo(writer, p.Name.ToCSharpIdentifier());

                isFirst = false;
            }

            if (!isFirst)
            {
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

    internal static void WriteLaunchReturnType(SourceWriter writer, IMethodSymbol method)
    {
        if (method.ReturnType.SpecialType != SpecialType.System_Void)
        {
            writer.AppendFullyQualifiedCsharpName(method.ReturnType);
            writer.Append("[] ");
        }
        else
        {
            writer.Append("void ");
        }
    }

    internal static void WriteOutParameterDefaults(SourceWriter writer, IMethodSymbol method)
    {
        foreach (var p in method.Parameters)
        {
            if (p.RefKind is not RefKind.Out || KernelParameterShape.Classify(p).IsArray)
            {
                continue;
            }

            writer.Append(p.Name.ToCSharpIdentifier());
            writer.AppendLine(" = default;");
        }
    }

    internal static void WriteArrayArgumentChecks(SourceWriter writer, IMethodSymbol method)
    {
        foreach (var p in method.Parameters)
        {
            var shape = KernelParameterShape.Classify(p);
            if (!shape.IsArray)
            {
                continue;
            }

            if (shape.IsOptional)
            {
                writer.Append("if ");
                using (writer.Parentheses())
                {
                    writer.Append($"{p.Name.ToCSharpIdentifier()} is {{ {shape.LengthMember}: 0 }}");
                }

                using (writer.Scope())
                {
                    writer.AppendLine($"{p.Name.ToCSharpIdentifier()} = null;");
                }
            }
            else if (shape.Container == ArrayContainerKind.Array)
            {
                writer.Append("if ");
                using (writer.Parentheses())
                {
                    writer.Append($"{p.Name.ToCSharpIdentifier()} is null");
                }

                using (writer.Scope())
                {
                    writer.AppendLine($"throw new global::System.ArgumentNullException(nameof({p.Name.ToCSharpIdentifier()}));");
                }

                writer.Append("if ");
                using (writer.Parentheses())
                {
                    writer.Append($"{p.Name.ToCSharpIdentifier()}.Length == 0");
                }

                using (writer.Scope())
                {
                    writer.AppendLine($"throw new global::System.ArgumentException(\"{EmptyArrayMessage}\", nameof({p.Name.ToCSharpIdentifier()}));");
                }
            }
            else if (shape.Container == ArrayContainerKind.ImmutableArray)
            {
                writer.Append("if ");
                using (writer.Parentheses())
                {
                    writer.Append($"{p.Name.ToCSharpIdentifier()}.IsDefault");
                }

                using (writer.Scope())
                {
                    writer.AppendLine($"throw new global::System.ArgumentNullException(nameof({p.Name.ToCSharpIdentifier()}));");
                }

                writer.Append("if ");
                using (writer.Parentheses())
                {
                    writer.Append($"{p.Name.ToCSharpIdentifier()}.Length == 0");
                }

                using (writer.Scope())
                {
                    writer.AppendLine($"throw new global::System.ArgumentException(\"{EmptyArrayMessage}\", nameof({p.Name.ToCSharpIdentifier()}));");
                }
            }
            else
            {
                writer.Append("if ");
                using (writer.Parentheses())
                {
                    writer.Append($"{shape.LengthExpression} == 0");
                }

                using (writer.Scope())
                {
                    writer.AppendLine($"throw new global::System.ArgumentException(\"{EmptyArrayMessage}\", nameof({p.Name.ToCSharpIdentifier()}));");
                }
            }
        }
    }

    internal static void WriteAutoResultAllocation(SourceWriter writer, IMethodSymbol method)
    {
        if (method.ReturnType.SpecialType != SpecialType.System_Void)
        {
            writer.AppendLine(
                "if (gridDimX == 0 || gridDimY == 0 || gridDimZ == 0 || blockDimX == 0 || blockDimY == 0 || blockDimZ == 0)" +
                " throw new global::System.ArgumentOutOfRangeException(\"Dimensions\", \"Launch dimensions must be greater than 0.\");");
            writer.AppendLine(
                $"uint {TotalThreads}; checked {{ {TotalThreads} = (gridDimX * gridDimY * gridDimZ) * (blockDimX * blockDimY * blockDimZ); }}");
            writer.Append($"var {GSharpInternalNames.ReturnValue.Buffer} = new ");
            writer.AppendFullyQualifiedCsharpName(method.ReturnType);
            writer.AppendLine($"[{TotalThreads}];");
        }
    }

    internal static void WriteAutoResultReturn(SourceWriter writer, IMethodSymbol method)
    {
        if (method.ReturnType.SpecialType != SpecialType.System_Void)
        {
            writer.AppendLine($"return {GSharpInternalNames.ReturnValue.Buffer};");
        }
    }
}
