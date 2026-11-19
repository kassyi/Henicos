namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

using NativeApi;

/// <summary>
/// Reports parameters of YAML <c>moduleName</c> bindings whose resolved C# types cannot be passed to a kernel
/// (such as structs with <c>LayoutKind.Auto</c>, classes, or fields containing them) with GSH0053 on the YAML
/// <c>type</c> line.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NativeApiModuleAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.NativeApiSchemaViolation);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var definitions = ReadModuleDefinitions(context.Options, context.CancellationToken);
        if (definitions.IsEmpty)
        {
            return;
        }

        AnalyzeDefinitions(context, definitions);
    }

    private static ImmutableArray<ModuleYamlEntry> ReadModuleDefinitions(
        AnalyzerOptions options,
        CancellationToken cancellationToken)
    {
        var builder = ImmutableArray.CreateBuilder<ModuleYamlEntry>();
        var emittedClasses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in options.AdditionalFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!options.AnalyzerConfigOptionsProvider.GetOptions(file)
                    .TryGetValue(NativeApiGenerator.GSharpApiMetadataKey, out var flag) ||
                !string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase) ||
                file.GetText(cancellationToken)?.ToString() is not { } text ||
                !NativeApiYamlReader.TryRead(text, out var definition, out _) ||
                definition.BindingKind == NativeApiBindingKind.Intrinsic)
            {
                continue;
            }

            var qualifiedName = YamlModuleClass.GetTypeName(definition.Namespace, definition.ClassName);
            if (!emittedClasses.Add(qualifiedName) ||
                definition.BindingKind != NativeApiBindingKind.Module)
            {
                continue;
            }

            builder.Add(new ModuleYamlEntry(file.Path, definition));
        }

        return builder.ToImmutable();
    }

    private static void AnalyzeDefinitions(
        CompilationAnalysisContext context,
        ImmutableArray<ModuleYamlEntry> definitions)
    {
        foreach (var entry in definitions)
        {
            var definition = entry.Definition;
            var typeName = YamlModuleClass.GetTypeName(definition.Namespace, definition.ClassName);
            var type = context.Compilation.GetTypeByMetadataName(typeName);
            if (type is null)
            {
                continue;
            }

            foreach (var method in definition.Methods)
            {
                if (FindUniqueMethod(type, method) is not { } symbol)
                {
                    continue;
                }

                AnalyzeMethodParameters(context, entry.FilePath, method, symbol);
            }
        }
    }

    // Matches by (name, parameter count, parameter names in order); returns null when 0 or >1 overloads match
    // (same name, arity, and parameter names differing only in type cannot be matched 1-to-1 by this rule).
    private static IMethodSymbol? FindUniqueMethod(INamedTypeSymbol type, ApiMethodDefinition method)
    {
        IMethodSymbol? matched = null;
        foreach (var member in type.GetMembers(method.Name))
        {
            if (member is not IMethodSymbol candidate || !ParametersMatchByCountAndNames(candidate, method))
            {
                continue;
            }

            if (matched is not null)
            {
                return null;
            }

            matched = candidate;
        }

        return matched;
    }

    private static bool ParametersMatchByCountAndNames(IMethodSymbol candidate, ApiMethodDefinition method)
    {
        if (candidate.Parameters.Length != method.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < candidate.Parameters.Length; i++)
        {
            if (!string.Equals(candidate.Parameters[i].Name, method.Parameters[i].Name, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void AnalyzeMethodParameters(
        CompilationAnalysisContext context,
        string filePath,
        ApiMethodDefinition method,
        IMethodSymbol symbol)
    {
        var fileName = Path.GetFileName(filePath);
        for (var i = 0; i < symbol.Parameters.Length; i++)
        {
            var parameter = symbol.Parameters[i];
            var launchSetting = ModuleKernelParameterTypes.GetLaunchSetting(parameter.Name);
            if (CudaImportPlanner.GetProblem(parameter, launchSetting, context.Compilation) is not { } reason)
            {
                continue;
            }

            var position = new LinePosition(Math.Max(0, method.Parameters[i].TypeLine - 1), 0);
            var location = Location.Create(filePath, default, new LinePositionSpan(position, position));
            var formattedType = IteratorTypeResolver.FormatType(parameter.Type);
            context.ReportDiagnostic(
                Diagnostic.Create(
                    GSharpDiagnostics.NativeApiSchemaViolation,
                    location,
                    fileName,
                    $"Parameter '{parameter.Name}' of kernel method '{method.Name}' has type " +
                    $"'{formattedType}', which a module launcher cannot pass: {reason}"
                )
            );
        }
    }

    private readonly record struct ModuleYamlEntry(string FilePath, NativeApiDefinition Definition);
}
