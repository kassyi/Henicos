namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>
/// Finds the functions that a <c>// gsharp:suppress GS0001</c> comment excludes from the register spill warning.
/// </summary>
public static class SpillSuppressionParser
{
    private const string SuppressGs0001Marker = "// gsharp:suppress GS0001";

    public static ISet<string> GetSuppressedSpillFunctions(string cuContent)
    {
        var suppressed = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(cuContent))
        {
            return suppressed;
        }

        var calleesByFunction = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        ScanFunctions(cuContent, suppressed, calleesByFunction, out var sawMarker);

        // Propagate suppression to device functions transitively called from a suppressed kernel/function,
        // and to recursive device functions (and the kernels that enter them).
        AddRecursiveFunctions(suppressed, calleesByFunction);
        while (PropagateSuppressionOnce(suppressed, calleesByFunction) > 0)
        {
        }

        if (sawMarker && suppressed.Count == 0)
        {
            suppressed.Add("*");
        }

        return suppressed;
    }

    // Reads the file line by line: records the callees of every defined function and the functions that a suppress
    // marker names. Sets sawMarker when a marker was seen.
    private static void ScanFunctions(
        string cuContent,
        HashSet<string> suppressed,
        Dictionary<string, HashSet<string>> calleesByFunction,
        out bool sawMarker)
    {
        sawMarker = false;
        var pendingSuppress = false;
        var currentFunction = "";
        var braceDepth = 0;

        using var reader = new StringReader(cuContent);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.AsSpan().Trim().Length == 0)
            {
                continue;
            }

            if (IsSuppressMarker(line))
            {
                sawMarker = true;
                pendingSuppress = true;
                continue;
            }

            if (braceDepth == 0)
            {
                StartFunction(line, ref currentFunction, ref pendingSuppress, suppressed, calleesByFunction);
            }
            else
            {
                MarkSuppressedFunction(line, ref pendingSuppress, suppressed);
            }

            CollectCalleesOfCurrentFunction(line, currentFunction, calleesByFunction);
            if (ClosesFunctionBody(line, ref braceDepth))
            {
                currentFunction = "";
            }
        }
    }

    private static bool IsSuppressMarker(string line) =>
        line.IndexOf(SuppressGs0001Marker, StringComparison.Ordinal) >= 0;

    // At depth 0 a line that defines a function (not a declaration ending in ';') makes it the current function and
    // takes over a pending marker; any other line may still be the signature a pending marker names.
    private static void StartFunction(
        string line,
        ref string currentFunction,
        ref bool pendingSuppress,
        HashSet<string> suppressed,
        Dictionary<string, HashSet<string>> calleesByFunction)
    {
        if (TryExtractDefinedFunctionName(line, out var fnName) &&
            !line.TrimEnd().EndsWith(";", StringComparison.Ordinal))
        {
            currentFunction = fnName;
            RegisterFunction(currentFunction, calleesByFunction);
            if (pendingSuppress)
            {
                suppressed.Add(currentFunction);
                pendingSuppress = false;
            }
        }
        else
        {
            MarkSuppressedFunction(line, ref pendingSuppress, suppressed);
        }
    }

    private static void MarkSuppressedFunction(string line, ref bool pendingSuppress, HashSet<string> suppressed)
    {
        if (pendingSuppress && TryExtractDefinedFunctionName(line, out var markedFn))
        {
            suppressed.Add(markedFn);
            pendingSuppress = false;
        }
    }

    private static void CollectCalleesOfCurrentFunction(
        string line,
        string currentFunction,
        Dictionary<string, HashSet<string>> calleesByFunction)
    {
        if (currentFunction.Length > 0 && calleesByFunction.TryGetValue(currentFunction, out var callees))
        {
            CollectGSharpCallees(line, currentFunction, callees);
        }
    }

    private static void RegisterFunction(string function, Dictionary<string, HashSet<string>> calleesByFunction)
    {
        if (!calleesByFunction.ContainsKey(function))
        {
            calleesByFunction[function] = new HashSet<string>(StringComparer.Ordinal);
        }
    }

    // Counts the braces of the line into the depth. Returns whether a closing brace brought the depth to zero.
    private static bool ClosesFunctionBody(string line, ref int braceDepth)
    {
        var closedFunction = false;
        foreach (var ch in line)
        {
            if (ch == '{')
            {
                braceDepth++;
            }
            else if (ch == '}')
            {
                braceDepth = Math.Max(0, braceDepth - 1);
                closedFunction |= braceDepth == 0;
            }
        }

        return closedFunction;
    }

    private static void AddRecursiveFunctions(
        HashSet<string> suppressed,
        Dictionary<string, HashSet<string>> calleesByFunction)
    {
        foreach (var kvp in calleesByFunction)
        {
            if (kvp.Key.StartsWith("__gsharp_fn_", StringComparison.Ordinal) &&
                CanReach(kvp.Key, kvp.Key, calleesByFunction, new HashSet<string>(StringComparer.Ordinal)))
            {
                suppressed.Add(kvp.Key);
            }
        }
    }

    // One pass: a suppressed function suppresses its callees, a kernel that calls a suppressed function is suppressed.
    // Returns how many functions it added.
    private static int PropagateSuppressionOnce(
        HashSet<string> suppressed,
        Dictionary<string, HashSet<string>> calleesByFunction)
    {
        var added = 0;
        foreach (var kvp in calleesByFunction)
        {
            if (suppressed.Contains(kvp.Key))
            {
                foreach (var callee in kvp.Value)
                {
                    if (suppressed.Add(callee))
                    {
                        added++;
                    }
                }
            }
            else if (kvp.Key.StartsWith("__gsharp_k_", StringComparison.Ordinal) && kvp.Value.Overlaps(suppressed))
            {
                suppressed.Add(kvp.Key);
                added++;
            }
        }

        return added;
    }

    private static bool CanReach(
        string current,
        string target,
        Dictionary<string, HashSet<string>> graph,
        HashSet<string> visited)
    {
        if (!graph.TryGetValue(current, out var nexts))
        {
            return false;
        }

        foreach (var next in nexts)
        {
            if (string.Equals(next, target, StringComparison.Ordinal))
            {
                return true;
            }

            if (visited.Add(next) && CanReach(next, target, graph, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractDefinedFunctionName(string line, out string functionName)
    {
        functionName = "";
        if (!TryFindIdentifierBeforeParenthesis(line, out var start, out var endExclusive))
        {
            return false;
        }

        var candidate = line[start..endExclusive];
        if (IsKeywordBeforeParenthesis(candidate) || !HasReturnTypeBefore(line, start))
        {
            return false;
        }

        functionName = candidate;
        return true;
    }

    // The identifier that ends right before the first '(' of the line, whitespace between them allowed.
    private static bool TryFindIdentifierBeforeParenthesis(string line, out int start, out int endExclusive)
    {
        start = 0;
        endExclusive = 0;
        var parenIdx = line.IndexOf('(');
        if (parenIdx <= 0)
        {
            return false;
        }

        var end = parenIdx - 1;
        while (end >= 0 && char.IsWhiteSpace(line[end]))
        {
            end--;
        }

        var first = end;
        while (first >= 0 && (char.IsLetterOrDigit(line[first]) || line[first] == '_'))
        {
            first--;
        }

        first++;
        if (first > end)
        {
            return false;
        }

        start = first;
        endExclusive = end + 1;
        return true;
    }

    private static bool IsKeywordBeforeParenthesis(string identifier) =>
        identifier is "if" or "for" or "while" or "switch" or "return" or "sizeof" or "alignof" or "defined"
            or "__declspec" or "__attribute__";

    // A definition has a return type or qualifier before the function identifier; a call statement does not.
    private static bool HasReturnTypeBefore(string line, int identifierStart) =>
        line.AsSpan(0, identifierStart).Trim().Length > 0;

    private static void CollectGSharpCallees(string line, string currentFunction, HashSet<string> callees)
    {
        const string FnPrefix = "__gsharp_fn_";
        var idx = 0;
        while (idx < line.Length)
        {
            var found = line.IndexOf(FnPrefix, idx, StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            var end = found + FnPrefix.Length;
            while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
            {
                end++;
            }

            var callee = line[found..end];
            // A call either is the same function inside its body (after the signature line) or another function
            // followed by '(' or '<'
            if (end < line.Length && (line[end] == '(' || line[end] == '<'))
            {
                if (!string.Equals(callee, currentFunction, StringComparison.Ordinal) ||
                    line.IndexOf("__device__", StringComparison.Ordinal) < 0)
                {
                    callees.Add(callee);
                }
            }

            idx = end;
        }
    }

    internal static bool IsFunctionSuppressed(string functionOrLine, ISet<string>? suppressedFunctions)
    {
        if (suppressedFunctions == null || suppressedFunctions.Count == 0 || string.IsNullOrEmpty(functionOrLine))
        {
            return false;
        }

        if (suppressedFunctions.Contains("*"))
        {
            return true;
        }

        foreach (var suppressed in suppressedFunctions)
        {
            if (suppressed.Length > 0 && functionOrLine.IndexOf(suppressed, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
