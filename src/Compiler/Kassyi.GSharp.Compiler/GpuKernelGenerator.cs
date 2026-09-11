using System.Text;
using Kassyi.GSharp.Compiler.Dialects;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Compiler;

internal record struct GeneratorConfig(
    string TargetDialect,
    bool IsAotEnabled,
    bool EmitSource,
    string? IntermediateOutputPath);

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

                options.GlobalOptions.TryGetValue("build_property.GSharpAotEnabled", out var aotStr);
                bool isAotEnabled = string.Equals(aotStr, "true", StringComparison.OrdinalIgnoreCase);

                options.GlobalOptions.TryGetValue("build_property.GSharpEmitSource", out var emitSrcStr);
                bool emitSource = isAotEnabled
                    ? string.Equals(emitSrcStr, "true", StringComparison.OrdinalIgnoreCase)
                    : true;

                options.GlobalOptions.TryGetValue("build_property.IntermediateOutputPath", out var objPath);
                options.GlobalOptions.TryGetValue("build_property.ProjectDir", out var projDir);
                var intermediatePath = !string.IsNullOrWhiteSpace(objPath) && !string.IsNullOrWhiteSpace(projDir)
                    ? Path.Combine(projDir, objPath)
                    : null;

                return new GeneratorConfig(dialect!, isAotEnabled, emitSource, intermediatePath);
            });

        var kernelMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s, KnownAttributes.GpuKernel),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx, KnownAttributes.GpuKernel))
            .Where(static m => m is not null);

        var deviceMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s, KnownAttributes.GpuDeviceFunction),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx, KnownAttributes.GpuDeviceFunction))
            .Where(static m => m is not null);

        var kernelsAndDevices = kernelMethods.Collect().Combine(deviceMethods.Collect());
        var compilationAndAll = context.CompilationProvider.Combine(kernelsAndDevices).Combine(configProvider);

        context.RegisterSourceOutput(compilationAndAll,
            static (spc, source) => Execute(source.Left.Left, source.Left.Right.Left, source.Left.Right.Right,
                source.Right, spc));
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node, string attributeName)
    {
        if (node is MethodDeclarationSyntax { AttributeLists.Count: > 0 } m)
        {
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

    private static IMethodSymbol? GetSemanticTargetForGeneration(GeneratorSyntaxContext context, string attributeName)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null)
        {
            return null;
        }

        return methodSymbol.HasAttribute(attributeName) ? methodSymbol : null;
    }

    private static void Execute(Compilation compilation, ImmutableArray<IMethodSymbol?> kernelMethods,
        ImmutableArray<IMethodSymbol?> deviceMethods, GeneratorConfig config, SourceProductionContext context)
    {
        if (kernelMethods.IsDefaultOrEmpty)
        {
            return;
        }

        ITranspilerDialect dialect;
        if (config.TargetDialect.Equals("Cuda", StringComparison.OrdinalIgnoreCase))
        {
            dialect = new GSharpCudaDialect();
        }
        else
        {
            dialect = new StandardCppDialect();
        }

        var commonDeviceSource = GenerateCommonDeviceSource(compilation, deviceMethods, context, dialect);
        var preludeSource = GetPreludeSource(context, dialect);

        if (preludeSource == null)
        {
            return;
        }

        using var preludeWriter = new SourceWriter();
        if (config.EmitSource)
        {
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
        }

        using var deviceWriter = new SourceWriter();
        if (config.EmitSource)
        {
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

        foreach (var method in kernelMethods.OfType<IMethodSymbol>()
                     .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
        {
            var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null)
            {
                continue;
            }

            var syntaxNode = syntaxRef.GetSyntax() as MethodDeclarationSyntax;
            if (syntaxNode == null)
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(syntaxRef.SyntaxTree);
            var source = MethodTranspiler.GenerateCudaSource(syntaxNode, semanticModel, context.ReportDiagnostic,
                isDevice: false, dialect);

            byte[]? aesKey = null;
            if (config.IsAotEnabled && !string.IsNullOrWhiteSpace(config.IntermediateOutputPath))
            {
                // Generate a random 256-bit (32-byte) key for this specific kernel
                aesKey = new byte[32];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    rng.GetBytes(aesKey);
                }

                // Write the full .cu file to intermediate output path for MSBuild target to pick up
                var fullCudaSource = preludeSource + commonDeviceSource + source;
                var fileName = $"{method.ContainingType.Name}_{method.Name}_Wrapper.g.cu";
                var filePath = Path.Combine(config.IntermediateOutputPath, fileName);
                var keyFilePath = filePath + ".key";

#pragma warning disable RS1035 // Do not do file IO in analyzers
                try
                {
                    if (!Directory.Exists(config.IntermediateOutputPath))
                    {
                        Directory.CreateDirectory(config.IntermediateOutputPath);
                    }

                    // Only write if changed to avoid unnecessary I/O
                    if (!File.Exists(filePath) || File.ReadAllText(filePath) != fullCudaSource)
                    {
                        File.WriteAllText(filePath, fullCudaSource);
                    }

                    // Always write the key file if AOT is enabled (since key is randomly generated per compilation)
                    File.WriteAllBytes(keyFilePath, aesKey);
                }
                catch
                {
                    // Ignore I/O errors during generator phase
                }
#pragma warning restore RS1035
            }

            // Generate the C# Wrapper Class
            var wrapperSource = WrapperEmitter.GenerateWrapperClass(method, source, config, aesKey);

            context.AddSource(method.ContainingType.Name + "_" + method.Name + "_Wrapper.g.cs",
                SourceText.From(wrapperSource, Encoding.UTF8));
        }
    }

    private static string GenerateCommonDeviceSource(Compilation compilation,
        ImmutableArray<IMethodSymbol?> deviceMethods, SourceProductionContext context,
        ITranspilerDialect dialect)
    {
        var deviceSourceBuilder = new StringBuilder();

        // Pass 1: Forward declarations
        foreach (var method in deviceMethods.OfType<IMethodSymbol>()
                     .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
        {
            var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null)
            {
                continue;
            }

            var syntaxNode = syntaxRef.GetSyntax() as MethodDeclarationSyntax;
            if (syntaxNode == null)
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(syntaxRef.SyntaxTree);
            var signatureCode = MethodTranspiler.GenerateCudaSignature(syntaxNode, semanticModel,
                context.ReportDiagnostic, isDevice: true, dialect);
            deviceSourceBuilder.AppendLine(signatureCode);
        }

        // Pass 2: Method bodies
        foreach (var method in deviceMethods.OfType<IMethodSymbol>()
                     .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
        {
            var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null)
            {
                continue;
            }

            var syntaxNode = syntaxRef.GetSyntax() as MethodDeclarationSyntax;
            if (syntaxNode == null)
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(syntaxRef.SyntaxTree);
            var deviceCode = MethodTranspiler.GenerateCudaSource(syntaxNode, semanticModel, context.ReportDiagnostic,
                isDevice: true, dialect);
            deviceSourceBuilder.AppendLine(deviceCode);
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
