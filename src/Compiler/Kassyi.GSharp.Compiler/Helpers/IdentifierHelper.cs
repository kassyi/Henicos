namespace Kassyi.GSharp.Compiler.Helpers;

/// <summary>
/// Provides helper methods for identifier transformations and symbol naming.
/// </summary>
internal static class IdentifierHelper
{
    /// <summary>
    /// Converts an assembly name or namespace into a safe C# identifier segment by replacing '.' with '_'.
    /// </summary>
    public static string ToSafeIdentifier(string name)
    {
        if (name.IndexOf('.') < 0)
        {
            return name;
        }

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '.')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
