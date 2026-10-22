namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Generates native bindings from YAML definitions supplied as
/// <c>&lt;AdditionalFiles Include="*.yml" GSharpApi="true" /&gt;</c>.
/// </summary>
/// <remarks>
/// <para>A definition with <c>libraryName</c> produces <c>[DllImport]</c> bindings; one with <c>moduleName</c>
/// produces launchers for kernels inside a fatbin / cubin / PTX module. Libraries that need probing
/// (platform candidates or relative paths) are routed through a single generated <c>DllImportResolver</c>.</para>
/// <para>All definitions are processed together so that duplicate classes can be reported instead of failing on a
/// hint-name collision; the step is cached on the file paths and contents only.</para>
/// </remarks>
[Generator]
public sealed class NativeApiGenerator : IIncrementalGenerator
{
    public const string GSharpApiMetadataKey = "build_metadata.AdditionalFiles.GSharpApi";

    public const string TrackingName = "NativeApiFiles";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair => IsGSharpApi(pair.Right.GetOptions(pair.Left)))
            .Select(static (pair, cancellationToken) =>
                new NativeApiSourceFile(pair.Left.Path, pair.Left.GetText(cancellationToken)?.ToString() ?? string.Empty))
            .Collect()
            .WithTrackingName(TrackingName);

        context.RegisterSourceOutput(files, Emit);
    }

    private static bool IsGSharpApi(AnalyzerConfigOptions options) =>
        options.TryGetValue(GSharpApiMetadataKey, out var value) &&
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static void Emit(SourceProductionContext context, ImmutableArray<NativeApiSourceFile> files)
    {
        var resolverDefinitions = ImmutableArray.CreateBuilder<NativeApiDefinition>();
        var emittedClasses = new HashSet<string>(StringComparer.Ordinal);
        var resolvedLibraries = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (!NativeApiYamlReader.TryRead(file.Text, out var definition, out var error))
            {
                Report(context, file, error);
                continue;
            }

            if (definition.BindingKind == NativeApiBindingKind.Intrinsic)
            {
                Report(context, file, new NativeApiReadError(NativeApiReadErrorKind.Schema, 1,
                    "Either 'libraryName' (host library) or 'moduleName' (kernel module) must be specified."));
                continue;
            }

            var qualifiedName = $"{definition.Namespace}.{definition.ClassName}";
            if (!emittedClasses.Add(qualifiedName))
            {
                Report(context, file, new NativeApiReadError(NativeApiReadErrorKind.Schema, definition.ClassNameLine,
                    $"Class '{qualifiedName}' is already defined by another native API definition."));
                continue;
            }

            var source = definition.BindingKind == NativeApiBindingKind.Library
                ? NativeBindingEmitter.EmitLibraryBindings(definition)
                : NativeBindingEmitter.EmitModuleBindings(definition);
            context.AddSource($"{qualifiedName}.NativeApi.g.cs", source);

            if (NativeBindingEmitter.RequiresResolver(definition) &&
                resolvedLibraries.Add(definition.LibraryName ?? string.Empty))
            {
                resolverDefinitions.Add(definition);
            }
        }

        if (resolverDefinitions.Count > 0)
        {
            context.AddSource($"{NativeBindingEmitter.ResolverClassName}.g.cs",
                NativeBindingEmitter.EmitLibraryResolver(resolverDefinitions.ToImmutable()));
        }
    }

    private static void Report(SourceProductionContext context, NativeApiSourceFile file, NativeApiReadError error)
    {
        var descriptor = error.Kind == NativeApiReadErrorKind.Syntax
            ? GSharpDiagnostics.NativeApiYamlSyntaxError
            : GSharpDiagnostics.NativeApiSchemaViolation;

        var position = new LinePosition(Math.Max(0, error.Line - 1), 0);
        var location = Location.Create(file.Path, default, new LinePositionSpan(position, position));
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, Path.GetFileName(file.Path), error.Message));
    }

    private readonly record struct NativeApiSourceFile(string Path, string Text);
}
