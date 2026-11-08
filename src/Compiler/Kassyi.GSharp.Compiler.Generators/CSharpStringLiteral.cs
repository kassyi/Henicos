namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>
/// Writes input text (YAML values, JSON strings, prelude sources) into generated C# as a string literal that holds the
/// text exactly.
/// </summary>
/// <remarks>
/// Every generator of this tool goes through <see cref="Quote"/>; the text is never put between <c>"</c> as it is,
/// where a backslash, a quote or a line break would end the literal or change its value.
/// </remarks>
internal static class CSharpStringLiteral
{
    /// <summary>
    /// Returns <paramref name="value"/> as a quoted C# string literal: <c>"</c>, <c>\</c>, line breaks and other control
    /// characters are escaped as Roslyn writes them (<see cref="SymbolDisplay.FormatLiteral(string, bool)"/>).
    /// <c>null</c>, which a JSON input can hold for a string property, is written as <c>""</c>.
    /// </summary>
    public static string Quote(string? value) => SymbolDisplay.FormatLiteral(value ?? string.Empty, quote: true);
}
