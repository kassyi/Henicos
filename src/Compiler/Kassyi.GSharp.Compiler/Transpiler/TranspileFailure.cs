namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// A construct that the transpiler could not emit: the diagnostic that describes it, its place in the user's source and
/// the message arguments.
/// </summary>
/// <remarks>
/// The transpiler reports nothing itself. The analyzer runs the transpiler as the generator does and reports each failure
/// as a diagnostic; the generator states the failures in the native source (<see cref="Format"/>), so a failure the
/// analyzer misses still stops the native build.
/// </remarks>
/// <param name="descriptor">The diagnostic that the analyzer reports for the failure.</param>
/// <param name="location">
/// The place in the user's source. A node of a lowered tree is mapped to its original span; a node without one (a
/// lowering bug) gives <see cref="Location.None"/>.
/// </param>
/// <param name="messageArgs">The arguments of the diagnostic's message.</param>
public readonly struct TranspileFailure(DiagnosticDescriptor descriptor, Location location, object?[] messageArgs)
{
    /// <summary>Stands for the position of a node that a lowering pass rebuilt without its original-span annotation.</summary>
    public const string NoOriginalSpan = "<no original span>";

    public DiagnosticDescriptor Descriptor { get; } = descriptor;

    public Location Location { get; } = location;

    public object?[] MessageArgs { get; } = messageArgs;

    /// <summary>The failure as the generated source states it: <c>file(line,column): ID: message</c>.</summary>
    public string Format()
    {
        var format = Descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture);
        var message = MessageArgs is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, format, MessageArgs)
            : format;

        if (!Location.IsInSource)
        {
            return $"{NoOriginalSpan}: {Descriptor.Id}: {message}";
        }

        var span = Location.GetLineSpan();
        var start = span.StartLinePosition;
        return $"{span.Path}({start.Line + 1},{start.Character + 1}): {Descriptor.Id}: {message}";
    }
}
