namespace Kassyi.GSharp.Compiler.NativeApi;

[Generator]
public class NativeCudaKernelsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var cuFiles = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".cu"));

        var kernelNames = cuFiles
            .Select(ExtractKernelNames)
            .Collect();

        context.RegisterSourceOutput(kernelNames, EmitNativeCudaKernels);
    }

    // Compared by value, so an edit of a .cu file that keeps its kernel names leaves the output cached.
    private static EquatableArray<string> ExtractKernelNames(AdditionalText file, CancellationToken cancellationToken)
    {
        var text = file.GetText(cancellationToken);
        if (text == null)
        {
            return ImmutableArray<string>.Empty.AsEquatableArray();
        }

        return CudaKernelNameScanner.Scan(text.ToString()).AsEquatableArray();
    }

    private static void EmitNativeCudaKernels(
        SourceProductionContext spc, ImmutableArray<EquatableArray<string>> arrays)
    {
        if (arrays.IsEmpty)
        {
            return;
        }

        var hasAny = arrays
            .AsStackEnumerable()
            .Any(static array => array.Length > 0);

        if (!hasAny)
        {
            return;
        }

        using var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader(enableNullable: false);
        writer.AppendLine();
        writer.AppendLine("internal static partial class NativeCudaKernels");
        using (writer.Scope())
        {
            EmitKernelConstants(writer, arrays);
        }

        spc.AddSource("NativeCudaKernels.g.cs", writer.ToString());
    }

    private static void EmitKernelConstants(SourceWriter writer, ImmutableArray<EquatableArray<string>> arrays)
    {
        var generated = new HashSet<string>();
        foreach (var array in arrays)
        {
            EmitArrayKernelConstants(writer, array.AsImmutableArray(), generated);
        }
    }

    private static void EmitArrayKernelConstants(
        SourceWriter writer, ImmutableArray<string> array, HashSet<string> generated)
    {
        foreach (var kernelName in array)
        {
            if (generated.Add(kernelName))
            {
                var literal = SymbolDisplay.FormatLiteral(kernelName, quote: true);
                writer.AppendLine($"public const string {kernelName.ToCSharpIdentifier()} = {literal};");
            }
        }
    }
}
