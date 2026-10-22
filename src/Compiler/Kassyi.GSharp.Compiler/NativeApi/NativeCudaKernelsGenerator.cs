namespace Kassyi.GSharp.Compiler.NativeApi;

[Generator]
public class NativeCudaKernelsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var cuFiles = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".cu"));

        var kernelNames = cuFiles.Select(static (file, cancellationToken) =>
        {
            var text = file.GetText(cancellationToken);
            if (text == null)
            {
                return ImmutableArray<string>.Empty;
            }

            var textStr = text.ToString();
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

                currentIndex += idx + searchSpan.Length;

                while (currentIndex < textStr.Length && char.IsWhiteSpace(textStr[currentIndex]))
                {
                    currentIndex++;
                }

                if (currentIndex + 4 > textStr.Length ||
                    !textStr.AsSpan(currentIndex, 4).SequenceEqual("void".AsSpan()))
                {
                    continue;
                }

                currentIndex += 4;
                while (currentIndex < textStr.Length && char.IsWhiteSpace(textStr[currentIndex]))
                {
                    currentIndex++;
                }

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
        }).Collect();

        context.RegisterSourceOutput(kernelNames, static (spc, arrays) =>
        {
            if (arrays.IsEmpty)
            {
                return;
            }

            bool hasAny = false;
            foreach (var array in arrays)
            {
                if (array.Length > 0)
                {
                    hasAny = true;
                    break;
                }
            }

            if (!hasAny)
            {
                return;
            }

            using var writer = new SourceWriter();
            writer.AppendGeneratedFileHeader(enableNullable: false);
            writer.AppendLine();
            writer.AppendLine("internal static partial class NativeCudaKernels");
            writer.AppendLine("{");

            var generated = new HashSet<string>();
            foreach (var array in arrays)
            {
                foreach (var kernelName in array)
                {
                    if (generated.Add(kernelName))
                    {
                        writer.AppendLine($"public const string {kernelName} = \"{kernelName}\";");
                    }
                }
            }

            writer.AppendLine("}");
            spc.AddSource("NativeCudaKernels.g.cs", writer.ToString());
        });
    }
}
