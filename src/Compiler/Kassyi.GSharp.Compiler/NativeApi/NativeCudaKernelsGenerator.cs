namespace Kassyi.GSharp.Compiler.NativeApi;

[Generator]
public class NativeCudaKernelsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var cuFiles = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".cu"));

        var kernelNames = cuFiles
            .Select(static (file, cancellationToken) => ExtractKernelNames(file, cancellationToken))
            .Collect();

        context.RegisterSourceOutput(kernelNames, static (spc, arrays) => EmitNativeCudaKernels(spc, arrays));
    }

    private static ImmutableArray<string> ExtractKernelNames(AdditionalText file, CancellationToken cancellationToken)
    {
        var text = file.GetText(cancellationToken);
        if (text == null)
        {
            return ImmutableArray<string>.Empty;
        }

        return ExtractKernelNamesFromSource(text.ToString());
    }

    private static ImmutableArray<string> ExtractKernelNamesFromSource(string textStr)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var searchSpan = "__global__".AsSpan();
        var currentIndex = 0;

        while (true)
        {
            var remaining = textStr.AsSpan(currentIndex);
            var idx = remaining.IndexOf(searchSpan, StringComparison.Ordinal);
            if (idx == -1)
            {
                break;
            }

            currentIndex = SkipWhitespace(textStr, currentIndex + idx + searchSpan.Length);
            if (currentIndex + 4 > textStr.Length ||
                !textStr.AsSpan(currentIndex, 4).SequenceEqual("void".AsSpan()))
            {
                continue;
            }

            currentIndex = SkipWhitespace(textStr, currentIndex + 4);
            var start = currentIndex;
            while (currentIndex < textStr.Length &&
                   (char.IsLetterOrDigit(textStr[currentIndex]) || textStr[currentIndex] == '_'))
            {
                currentIndex++;
            }

            if (currentIndex > start)
            {
                builder.Add(textStr[start..currentIndex]);
            }
        }

        return builder.ToImmutable();
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static void EmitNativeCudaKernels(
        SourceProductionContext spc, ImmutableArray<ImmutableArray<string>> arrays)
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

    private static void EmitKernelConstants(SourceWriter writer, ImmutableArray<ImmutableArray<string>> arrays)
    {
        var generated = new HashSet<string>();
        foreach (var array in arrays)
        {
            EmitArrayKernelConstants(writer, array, generated);
        }
    }

    private static void EmitArrayKernelConstants(
        SourceWriter writer, ImmutableArray<string> array, HashSet<string> generated)
    {
        foreach (var kernelName in array)
        {
            if (generated.Add(kernelName))
            {
                writer.AppendLine($"public const string {kernelName} = \"{kernelName}\";");
            }
        }
    }
}
