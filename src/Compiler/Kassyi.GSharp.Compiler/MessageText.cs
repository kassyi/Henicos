namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The C# spelling of a user symbol in the text of a message for people: a diagnostic, or the message of a run-time
/// error that generated code carries as a string literal. Never a C++ identifier: a name that the generated code
/// declares or references goes through <c>CppNameResolver</c>.
/// </summary>
internal static class MessageText
{
    /// <summary>The name of <paramref name="symbol"/> as the user wrote it, for message text only.</summary>
    public static string Name(ISymbol symbol) => symbol.Name;
}
