namespace Kassyi.GSharp.Compiler;

/// <summary>A zero-allocation inline scope that appends an opening string on creation and a closing string on disposal without adding new lines.</summary>
public readonly ref struct SourceWriterInlineScope
{
    private readonly SourceWriter _writer;
    private readonly string _closingText;

    public SourceWriterInlineScope(SourceWriter writer, string openingText, string closingText)
    {
        _writer = writer;
        _closingText = closingText;
        _writer.Append(openingText);
    }

    public void Dispose() => _writer.Append(_closingText);
}
