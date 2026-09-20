using System.Security.Cryptography;
using Kassyi.GSharp.Compiler.Dialects;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Compiler;

internal record struct GeneratorConfig(
    string TargetDialect,
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

                return new GeneratorConfig(dialect!, emitSource, intermediatePath);
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

        var distinctKernelMethods = kernelMethods.OfType<IMethodSymbol>()
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default).ToList();

        ITranspilerDialect dialect = config.TargetDialect.Equals("Cuda", StringComparison.OrdinalIgnoreCase)
            ? new GSharpCudaDialect()
            : new StandardCppDialect();

        var commonDeviceSource = GenerateCommonDeviceSource(compilation, deviceMethods, context, dialect);
        var preludeSource = GetPreludeSource(context, dialect);

        if (preludeSource == null)
        {
            return;
        }

        EmitSharedSources(context, config, preludeSource, commonDeviceSource);

        var allKernelsSource = new StringBuilder();
        foreach (var method in distinctKernelMethods)
        {
            var kernelSource = ProcessKernelMethod(method, compilation, config, context, dialect);
            allKernelsSource.AppendLine(kernelSource);
        }

        var assemblyName = compilation.AssemblyName ?? "UnknownAssembly";
        var safeAssemblyName = assemblyName.Replace(".", "_");
        WriteUnityAotArtifact(safeAssemblyName, config, preludeSource, commonDeviceSource, allKernelsSource.ToString(), dialect);

        if (config.TargetDialect.Equals("Cuda", StringComparison.OrdinalIgnoreCase))
        {
            var contextSource = WrapperEmitter.GenerateModuleContextClass(assemblyName, distinctKernelMethods);
            context.AddSource($"GSharpModuleContext_{safeAssemblyName}.g.cs",
                SourceText.From(contextSource, Encoding.UTF8));
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

    private static string ProcessKernelMethod(IMethodSymbol method, Compilation compilation, GeneratorConfig config,
        SourceProductionContext context, ITranspilerDialect dialect)
    {
        var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();

        if (syntaxRef?.GetSyntax() is not MethodDeclarationSyntax syntaxNode)
        {
            return string.Empty;
        }

        var semanticModel = compilation.GetSemanticModel(syntaxRef.SyntaxTree);
        var source = MethodTranspiler.GenerateCudaSource(syntaxNode, semanticModel, context.ReportDiagnostic,
            isDevice: false, dialect);

        var wrapperSource = WrapperEmitter.GenerateWrapperClass(method, source, config);
        context.AddSource(method.ContainingType.Name + "_" + method.Name + "_Wrapper.g.cs",
            SourceText.From(wrapperSource, Encoding.UTF8));

        return source;
    }

    private static void WriteUnityAotArtifact(string safeAssemblyName, GeneratorConfig config, string preludeSource, string commonDeviceSource, string allKernelsSource, ITranspilerDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(config.IntermediateOutputPath))
        {
            return;
        }

        var fullSource = preludeSource + commonDeviceSource + allKernelsSource;
        var ext = dialect.IsCuda ? ".g.cu" : ".g.cpp";
        var fileName = $"Kassyi.GSharp.Kernels_{safeAssemblyName}{ext}";
        var filePath = Path.Combine(config.IntermediateOutputPath!, fileName);

#pragma warning disable RS1035 // Do not do file IO in analyzers
        try
        {
            if (!Directory.Exists(config.IntermediateOutputPath))
            {
                Directory.CreateDirectory(config.IntermediateOutputPath!);
            }

            if (!File.Exists(filePath) || File.ReadAllText(filePath) != fullSource)
            {
                File.WriteAllText(filePath, fullSource);
            }
        }
        catch
        {
            // Ignore I/O errors during generator phase
        }
#pragma warning restore RS1035
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
