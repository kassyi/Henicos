namespace Kassyi.GSharp.Compiler;

/// <summary>A zero-allocation scope that appends an opening string on creation and a closing string on disposal.</summary>
public readonly ref struct SourceWriterScope
{
    private readonly SourceWriter _writer;
    private readonly string _closingText;

    public SourceWriterScope(SourceWriter writer, string openingText, string closingText)
    {
        _writer = writer;
        _closingText = closingText;
        if (string.IsNullOrEmpty(openingText))
        {
            return;
        }

        _writer.AppendLine(openingText);
#pragma warning disable CA1865
        if (openingText != "{" && !openingText.EndsWith("{", StringComparison.Ordinal))
#pragma warning restore CA1865
        {
            _writer.AppendLine("{");
        }
    }

    public void Dispose()
    {
        if (!string.IsNullOrEmpty(_closingText))
        {
            _writer.AppendLine(_closingText);
        }
    }
}
