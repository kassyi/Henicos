namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>
/// The functions whose register spills are not reported as GS0001: the names that the generator lists on the
/// <see cref="SpillSuppressionLine"/> of a CUDA module, compared whole with the function that a ptxas line names.
/// </summary>
/// <remarks>
/// The generator decides the names from its own call graph (the kernels under <c>#pragma warning disable GS0001</c>
/// and the device functions that only they reach), so nothing here reads the generated C++ otherwise. A ptxas line
/// names a kernel as it is (<c>extern "C"</c>) and a device function in its Itanium-mangled form
/// (<c>_Z&lt;length&gt;&lt;name&gt;&lt;parameters&gt;</c>), whose name is the <c>length</c> characters after the
/// digits.
/// </remarks>
public static class SpillSuppression
{
    private const string InFunction = "function '";
    private const string FunctionProperties = "Function properties for ";

    /// <summary>The names on the suppression lines of <paramref name="cuContent"/>; empty when it has none.</summary>
    public static ISet<string> ReadSuppressedFunctions(string cuContent)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(cuContent))
        {
            return names;
        }

        using var reader = new StringReader(cuContent);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith(SpillSuppressionLine.Marker + " ", StringComparison.Ordinal))
            {
                AddNames(line.AsSpan(SpillSuppressionLine.Marker.Length + 1), names);
            }
        }

        return names;
    }

    /// <summary>
    /// Whether the function that <paramref name="ptxasLine"/> names (<c>... function '&lt;name&gt;'</c> or
    /// <c>Function properties for &lt;name&gt;</c>) is one of <paramref name="suppressedFunctions"/>.
    /// </summary>
    public static bool IsSuppressed(string ptxasLine, ISet<string>? suppressedFunctions) =>
        suppressedFunctions is { Count: > 0 } &&
        TryReadFunctionName(ptxasLine, out var name) &&
        suppressedFunctions.Contains(name);

    /// <summary>The C++ name of the function that a ptxas line names, demangled to its identifier.</summary>
    internal static bool TryReadFunctionName(string ptxasLine, out string name)
    {
        name = "";
        if (string.IsNullOrEmpty(ptxasLine))
        {
            return false;
        }

        ReadOnlySpan<char> symbol;
        var quoted = ptxasLine.IndexOf(InFunction, StringComparison.Ordinal);
        var properties = ptxasLine.IndexOf(FunctionProperties, StringComparison.Ordinal);
        if (quoted >= 0)
        {
            var rest = ptxasLine.AsSpan(quoted + InFunction.Length);
            var end = rest.IndexOf('\'');
            if (end <= 0)
            {
                return false;
            }

            symbol = rest.Slice(0, end);
        }
        else if (properties >= 0)
        {
            symbol = ptxasLine.AsSpan(properties + FunctionProperties.Length).Trim();
        }
        else
        {
            return false;
        }

        symbol = Demangle(symbol);
        if (symbol.IsEmpty)
        {
            return false;
        }

        name = symbol.ToString();
        return true;
    }

    // _Z<length><identifier>... (also _ZL for a function with internal linkage) gives the identifier. A local entity,
    // _ZZ<length><identifier>...E<entity> (the lambda of a local function; _ZZZ for one nested in another), gives the
    // identifier of the function that encloses it, so its spills count as that function's. Any other symbol is the
    // name itself; a nested name (_ZN...E) is not emitted by G#, so it stays as it is and matches no name.
    private static ReadOnlySpan<char> Demangle(ReadOnlySpan<char> symbol)
    {
        if (!symbol.StartsWith("_Z".AsSpan(), StringComparison.Ordinal))
        {
            return symbol;
        }

        var position = 2;
        while (position < symbol.Length && symbol[position] == 'Z')
        {
            position++;
        }

        if (position < symbol.Length && symbol[position] == 'L')
        {
            position++;
        }

        var length = 0;
        var digits = 0;
        while (position + digits < symbol.Length && char.IsDigit(symbol[position + digits]))
        {
            length = length * 10 + (symbol[position + digits] - '0');
            digits++;
        }

        var start = position + digits;
        return digits == 0 || length == 0 || start + length > symbol.Length ? symbol : symbol.Slice(start, length);
    }

    private static void AddNames(ReadOnlySpan<char> rest, HashSet<string> names)
    {
        while (rest.Length > 0)
        {
            rest = rest.TrimStart();
            var end = rest.IndexOf(' ');
            var name = end < 0 ? rest : rest.Slice(0, end);
            if (name.Length > 0)
            {
                names.Add(name.ToString());
            }

            rest = end < 0 ? ReadOnlySpan<char>.Empty : rest.Slice(end);
        }
    }
}
