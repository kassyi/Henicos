namespace Kassyi.GSharp.Compiler;

/// <summary>A zero-allocation scope that wraps content in a C++ immediately invoked lambda expression.</summary>
public readonly ref struct SourceWriterImmediatelyInvokedLambdaScope
{
    private readonly SourceWriter _writer;

    public SourceWriterImmediatelyInvokedLambdaScope(SourceWriter writer, string? returnType = null)
    {
        _writer = writer;
        if (returnType == null)
        {
            _writer.AppendLine("([&]() {");
        }
        else
        {
            _writer.AppendLine($"([&]() -> {returnType} {{");
        }
    }

    public void Dispose() => _writer.Append("}())");
}
