using System.Text;

namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>
/// Quotes one argument for <see cref="ProcessStartInfo.Arguments"/>, which <see cref="Process"/> splits by the
/// rules of the Windows C runtime on every OS: whitespace separates arguments, double quotes group them, and
/// backslashes are literal unless a double quote follows them.
/// </summary>
internal static class CommandLineQuoting
{
    private static readonly char[] s_needsQuotes = [' ', '\t', '\n', '\v', '"'];

    /// <summary>Returns <paramref name="argument"/> as is when it needs no quotes, else quoted and escaped.</summary>
    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny(s_needsQuotes) < 0)
        {
            return argument;
        }

        var quoted = new StringBuilder(argument.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            // Backslashes before a quote are doubled and the quote is escaped; elsewhere they stay as they are.
            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes).Append(c);
            backslashes = 0;
        }

        // Backslashes before the closing quote are doubled so that it still closes.
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
