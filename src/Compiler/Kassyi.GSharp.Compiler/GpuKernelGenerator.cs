using System.Security.Cryptography;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Helpers;
using Kassyi.GSharp.Compiler.Models;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Compiler;

/// <summary>Roslyn incremental source generator that transpiles C# GPU kernels to CUDA C++ and emits host invocations.</summary>
/// <remarks><para>Implements: <b>GEN-01, GEN-02, GEN-03, GEN-04, GEN-05, GEN-06, GE-07, GEN-08, GEN-09, GEN-10, GEN-11, GEN-12, GEN-13, GEN-14, GEN-17, GEN-18, GEN-19, GEN-25, GEN-28, GEN-29, GEN-31, GEN-32</b></para></remarks>
[Generator]
public class GpuKernelGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var configProvider = context.AnalyzerConfigOptionsProvider
            .Select((options, _) =>
            {
                options.GlobalOptions.TryGetValue("build_property.GSharpTargetDialect", out var dialect);
                if (string.IsNullOrWhiteSpace(dialect))
                {
                    dialect = "Cpu";
                }

                options.GlobalOptions.TryGetValue("build_property.GSharpEmitSource", out var emitSrcStr);
                bool emitSource = string.IsNullOrWhiteSpace(emitSrcStr) ||
                                  string.Equals(emitSrcStr, "true", StringComparison.OrdinalIgnoreCase);

                options.GlobalOptions.TryGetValue("build_property.IntermediateOutputPath", out var objPath);
                options.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var projDir);
                if (string.IsNullOrWhiteSpace(projDir))
                {
                    options.GlobalOptions.TryGetValue("build_property.ProjectDir", out projDir);
                }

                var intermediatePath = !string.IsNullOrWhiteSpace(objPath) && !string.IsNullOrWhiteSpace(projDir)
                    ? Path.Combine(projDir, objPath)
                    : null;

                options.GlobalOptions.TryGetValue("build_property.GSharpAotEncryptionKey", out var customEncryptionKey);

                return new GeneratorConfig(dialect!, emitSource, intermediatePath, customEncryptionKey);
            });

        var kernelMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s, KnownAttributes.GpuKernel),
                transform: static (ctx, _) => TranspileKernel(ctx, KnownAttributes.GpuKernel))
            .Where(static m => m.HasValue)
            .Select(static (m, _) => m!.Value);

        var deviceMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s, KnownAttributes.GpuDeviceFunction),
                transform: static (ctx, _) => TranspileDeviceMethod(ctx, KnownAttributes.GpuDeviceFunction))
            .Where(static m => m.HasValue)
            .Select(static (m, _) => m!.Value);

        var kernelsAndDevices = kernelMethods.Collect().Combine(deviceMethods.Collect());
        var allData = kernelsAndDevices.Combine(configProvider);

        context.RegisterSourceOutput(allData,
            static (spc, source) => Execute(source.Left.Left, source.Left.Right, source.Right, spc));
    }

    private static TranspiledKernelResult? TranspileKernel(GeneratorSyntaxContext context, string attributeName)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null || !methodSymbol.HasAttribute(attributeName))
        {
            return null;
        }

        var diagnostics = new List<Diagnostic>();
        void reportDiagnostic(Diagnostic d) => diagnostics.Add(d);

        var cudaDialect = new GSharpCudaDialect();
        var cppDialect = new StandardCppDialect();

        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDeclaration, context.SemanticModel, reportDiagnostic,
            isDevice: false, cudaDialect);
        var cppSource = MethodTranspiler.GenerateCudaSource(methodDeclaration, context.SemanticModel, _ =>
        {
        }, isDevice: false, cppDialect);

        string? keyProvider = ResolveKeyProvider(methodSymbol);

        var equatableDiagnostics = diagnostics.Select(d => d.AsEquatable()).ToImmutableArray().AsEquatableArray();

        return new TranspiledKernelResult(
            methodSymbol,
            methodSymbol.Name,
            methodSymbol.ContainingType.Name,
            methodSymbol.ContainingNamespace?.GetFullNamespaceName(),
            cudaSource,
            cppSource,
            keyProvider,
            equatableDiagnostics
        );
    }

    private static TranspiledDeviceMethodResult? TranspileDeviceMethod(GeneratorSyntaxContext context,
        string attributeName)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null || !methodSymbol.HasAttribute(attributeName))
        {
            return null;
        }

        var diagnostics = new List<Diagnostic>();

        var cudaDialect = new GSharpCudaDialect();
        var cppDialect = new StandardCppDialect();

        var cudaSignature = MethodTranspiler.GenerateCudaSignature(methodDeclaration, context.SemanticModel,
            reportDiagnostic, isDevice: true, cudaDialect);
        var cppSignature = MethodTranspiler.GenerateCudaSignature(methodDeclaration, context.SemanticModel, _ =>
        {
        }, isDevice: true, cppDialect);

        var cudaBody = MethodTranspiler.GenerateCudaSource(methodDeclaration, context.SemanticModel, reportDiagnostic,
            isDevice: true, cudaDialect);
        var cppBody = MethodTranspiler.GenerateCudaSource(methodDeclaration, context.SemanticModel, _ =>
        {
        }, isDevice: true, cppDialect);

        var equatableDiagnostics = diagnostics.Select(d => d.AsEquatable()).ToImmutableArray().AsEquatableArray();

        return new TranspiledDeviceMethodResult(
            methodSymbol,
            methodSymbol.Name,
            methodSymbol.ContainingType.Name,
            methodSymbol.ContainingNamespace?.GetFullNamespaceName(),
            cudaSignature,
            cudaBody,
            cppSignature,
            cppBody,
            equatableDiagnostics);

        void reportDiagnostic(Diagnostic d) => diagnostics.Add(d);
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node, string attributeName)
    {
        if (node is not MethodDeclarationSyntax { AttributeLists.Count: > 0 } m)
        {
            return false;
        }

        foreach (var attributeList in m.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var simpleName = GetSimpleName(attribute.Name);
                if (simpleName != null &&
                    KnownAttributes.IsAttributeName(simpleName.Identifier.ValueText, attributeName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static SimpleNameSyntax? GetSimpleName(NameSyntax name) => name switch
    {
        SimpleNameSyntax simple => simple,
        QualifiedNameSyntax qualified => qualified.Right,
        AliasQualifiedNameSyntax alias => alias.Name,
        _ => null
    };

    private static void Execute(ImmutableArray<TranspiledKernelResult> kernelMethods,
        ImmutableArray<TranspiledDeviceMethodResult> deviceMethods, GeneratorConfig config,
        SourceProductionContext context)
    {
        if (kernelMethods.IsDefaultOrEmpty)
        {
            return;
        }

        bool isCuda = config.TargetDialect.Equals("Cuda", StringComparison.OrdinalIgnoreCase);
        ITranspilerDialect dialect = isCuda ? new GSharpCudaDialect() : new StandardCppDialect();

        var distinctDeviceMethods = deviceMethods.Distinct().ToList();
        var commonDeviceSource =
            GenerateCommonDeviceSource(distinctDeviceMethods, context, isCuda);
        var preludeSource = GetPreludeSource(context, dialect);

        if (preludeSource == null)
        {
            return;
        }

        EmitSharedSources(context, config, preludeSource, commonDeviceSource);

        byte[]? aesKey = null;
        if (!string.IsNullOrWhiteSpace(config.IntermediateOutputPath))
        {
            aesKey = ResolveEncryptionKey(config);
        }

        // Use Distinct by fully qualified method name to avoid duplicates if partial classes define it twice (which is illegal in C# but AST parses it).
        // For IMethodSymbol, SymbolEqualityComparer.Default is fine here because they are from the SAME cached output if unchanged.
        foreach (var method in kernelMethods.Distinct())
        {
            ProcessKernelMethod(method, config, context, dialect, preludeSource, commonDeviceSource, isCuda, aesKey);
        }
    }

    private static void EmitSharedSources(SourceProductionContext context, GeneratorConfig config, string preludeSource,
        string commonDeviceSource)
    {
        if (!config.EmitSource)
        {
            return;
        }

        using var preludeWriter = new SourceWriter();
        preludeWriter.AppendLine("// <auto-generated/>");
        preludeWriter.AppendLine("namespace Kassyi.GSharp.Generated");
        using (preludeWriter.Scope())
        {
            preludeWriter.AppendLine($"internal static class GSharpShared{config.TargetDialect}Prelude");
            using (preludeWriter.Scope())
            {
                preludeWriter.Append("public const string Source = @\"");
                foreach (var c in preludeSource)
                {
                    if (c == '"')
                    {
                        preludeWriter.Append("\"\"");
                    }
                    else
                    {
                        preludeWriter.Append(c);
                    }
                }

                preludeWriter.AppendLine("\";");
            }
        }

        context.AddSource($"GSharpShared{config.TargetDialect}Prelude.g.cs",
            SourceText.From(preludeWriter.ToString(), Encoding.UTF8));

        using var deviceWriter = new SourceWriter();
        deviceWriter.AppendLine("// <auto-generated/>");
        deviceWriter.AppendLine("namespace Kassyi.GSharp.Generated");
        using (deviceWriter.Scope())
        {
            deviceWriter.AppendLine($"internal static class GSharpShared{config.TargetDialect}DeviceCode");
            using (deviceWriter.Scope())
            {
                deviceWriter.Append("public const string Source = @\"");
                foreach (var c in commonDeviceSource)
                {
                    if (c == '"')
                    {
                        deviceWriter.Append("\"\"");
                    }
                    else
                    {
                        deviceWriter.Append(c);
                    }
                }

                deviceWriter.AppendLine("\";");
            }
        }

        context.AddSource($"GSharpShared{config.TargetDialect}DeviceCode.g.cs",
            SourceText.From(deviceWriter.ToString(), Encoding.UTF8));
    }

    private static void ProcessKernelMethod(TranspiledKernelResult method, GeneratorConfig config,
        SourceProductionContext context, ITranspilerDialect dialect, string preludeSource, string commonDeviceSource,
        bool isCuda, byte[]? aesKey)
    {
        foreach (var diag in method.Diagnostics)
        {
            context.ReportDiagnostic(diag.Diagnostic);
        }

        string source = isCuda ? method.CudaSource : method.CppSource;
        string? keyProvider = method.KeyProvider;

        if (!string.IsNullOrWhiteSpace(config.IntermediateOutputPath))
        {
            if (keyProvider != null && string.IsNullOrWhiteSpace(config.CustomEncryptionKey))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "GEN002", "Missing Encryption Key",
                        $"Method '{method.MethodName}' specifies an EncryptionKeyProvider, but no build-time key was provided via <GSharpAotEncryptionKey> MSBuild property.",
                        "GSharp", DiagnosticSeverity.Error, true),
                    method.MethodSymbol.Locations.FirstOrDefault() ?? Location.None));
            }

            WriteAotArtifacts(method, config, aesKey!, preludeSource, commonDeviceSource, source, dialect);
        }

        var wrapperSource =
            WrapperEmitter.GenerateWrapperClass(method.MethodSymbol, source, config, aesKey, keyProvider);
        context.AddSource(method.ClassName + "_" + method.MethodName + "_Wrapper.g.cs",
            SourceText.From(wrapperSource, Encoding.UTF8));
    }

    private static string? ResolveKeyProvider(IMethodSymbol method)
    {
        var attrData = method.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass != null &&
            KnownAttributes.IsAttributeName(a.AttributeClass.Name, KnownAttributes.GpuKernel));
        if (attrData == null)
        {
            return null;
        }

        foreach (var namedArg in attrData.NamedArguments)
        {
            if (namedArg is { Key: "EncryptionKeyProvider", Value.Value: string providerName } &&
                !string.IsNullOrWhiteSpace(providerName))
            {
                return providerName;
            }
        }

        return null;
    }

    private static byte[] ResolveEncryptionKey(GeneratorConfig config)
    {
        var aesKey = new byte[32];
        if (!string.IsNullOrWhiteSpace(config.CustomEncryptionKey))
        {
            try
            {
                var decoded = Convert.FromBase64String(config.CustomEncryptionKey!);
                if (decoded.Length == 32)
                {
                    return decoded;
                }
            }
            catch
            {
                // Fallback to hashing below
            }

            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(config.CustomEncryptionKey!));
        }

        using (var sha = SHA256.Create())
        {
            var defaultKeyHash =
                sha.ComputeHash(Encoding.UTF8.GetBytes("GSharp_Default_VBO_Key_Fallback_" + config.TargetDialect));
            Array.Copy(defaultKeyHash, aesKey, 32);
        }

        return aesKey;
    }

    private static void WriteAotArtifacts(TranspiledKernelResult method, GeneratorConfig config, byte[] aesKey,
        string preludeSource, string commonDeviceSource, string kernelSource, ITranspilerDialect dialect)
    {
        var fullCudaSource = preludeSource + commonDeviceSource + kernelSource;
        var ext = dialect.IsCuda ? ".g.cu" : ".g.cpp";
        var fileName = $"{method.ClassName}_{method.MethodName}{ext}";
        var filePath = Path.Combine(config.IntermediateOutputPath!, fileName);
        var keyFilePath = filePath + ".key";

#pragma warning disable RS1035 // Do not do file IO in analyzers
        try
        {
            if (!Directory.Exists(config.IntermediateOutputPath))
            {
                Directory.CreateDirectory(config.IntermediateOutputPath!);
            }

            if (!File.Exists(filePath) || File.ReadAllText(filePath) != fullCudaSource)
            {
                File.WriteAllText(filePath, fullCudaSource);
            }

            File.WriteAllBytes(keyFilePath, aesKey);
        }
        catch
        {
            // Ignore I/O errors during generator phase
        }
#pragma warning restore RS1035
    }

    private static string GenerateCommonDeviceSource(IReadOnlyList<TranspiledDeviceMethodResult> deviceMethods,
        SourceProductionContext context, bool isCuda)
    {
        var deviceSourceBuilder = new StringBuilder();

        // Pass 1: Forward declarations
        foreach (var method in deviceMethods)
        {
            foreach (var diag in method.Diagnostics)
            {
                context.ReportDiagnostic(diag.Diagnostic);
            }

            deviceSourceBuilder.AppendLine(isCuda ? method.CudaSignature : method.CppSignature);
        }

        // Pass 2: Method bodies
        foreach (var method in deviceMethods)
        {
            deviceSourceBuilder.AppendLine(isCuda ? method.CudaBody : method.CppBody);
        }

        return deviceSourceBuilder.ToString();
    }

    private static string? GetPreludeSource(SourceProductionContext context, ITranspilerDialect dialect)
    {
        var resourceFileName = dialect.IsCuda ? "Prelude.cu" : "Prelude.cpp";
        var assembly = typeof(GpuKernelGenerator).Assembly;
        var resources = assembly.GetManifestResourceNames().Where(n => n.EndsWith(resourceFileName)).ToList();

        if (resources.Count != 1)
        {
            var diagnostic = Diagnostic.Create(
                GSharpDiagnostics.PreludeMissing,
                Location.None,
                assembly.FullName,
                resources.Count);
            context.ReportDiagnostic(diagnostic);
            return null;
        }

        var resourceName = resources.Single();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            var diagnostic = Diagnostic.Create(
                GSharpDiagnostics.PreludeMissing,
                Location.None,
                assembly.FullName,
                0);
            context.ReportDiagnostic(diagnostic);
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd() + "\n";
    }
}
