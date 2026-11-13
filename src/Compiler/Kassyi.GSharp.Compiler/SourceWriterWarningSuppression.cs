namespace Kassyi.GSharp.Compiler;

/// <summary>
/// A zero-allocation scope that disables a C# warning in generated code on creation and restores it on disposal.
/// </summary>
public readonly ref struct SourceWriterWarningSuppression
{
    private readonly SourceWriter _writer;
    private readonly string? _id;

    public SourceWriterWarningSuppression(SourceWriter writer, string id, string? reason, bool condition)
    {
        _writer = writer;
        _id = condition ? id : null;
        if (!condition)
        {
            return;
        }

        if (reason is null)
        {
            _writer.AppendLine($"#pragma warning disable {id}");
        }
        else
        {
            _writer.AppendLine($"#pragma warning disable {id} // {reason}");
        }
    }

    public void Dispose()
    {
        if (_id is not null)
        {
            _writer.AppendLine($"#pragma warning restore {_id}");
        }
    }
}
