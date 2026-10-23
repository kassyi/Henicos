namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Formats why the transpiler could not emit a construct. The analyzers report the matching diagnostic; the transpiler
/// keeps only this text, which the generated source carries so that a check the analyzers miss still fails the native
/// build instead of passing silently.
/// </summary>
internal static class TranspileFailure
{
    /// <summary>Stands for the position of a node that a lowering pass rebuilt without its original-span annotation.</summary>
    public const string NoOriginalSpan = "<no original span>";

    // <file>(<line>,<column>): <id>: <message>
    public static string Format(DiagnosticDescriptor descriptor, Location? location, object?[]? messageArgs)
    {
        var format = descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture);
        var message = messageArgs is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, format, messageArgs)
            : format;

        if (location is not { IsInSource: true })
        {
            return $"{NoOriginalSpan}: {descriptor.Id}: {message}";
        }

        var span = location.GetLineSpan();
        var start = span.StartLinePosition;
        return $"{span.Path}({start.Line + 1},{start.Character + 1}): {descriptor.Id}: {message}";
    }
}
