namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>Finds the functions that a <c>// gsharp:suppress GS0001</c> comment excludes from the register spill warning.</summary>
internal static class SpillSuppressionParser
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
        var sawMarker = ScanFunctions(cuContent, suppressed, calleesByFunction);

        // Propagate suppression to device functions transitively called from a suppressed kernel/function,
        // and to recursive device functions (and the kernels that enter them).
        AddRecursiveFunctions(suppressed, calleesByFunction);
        while (PropagateSuppression(suppressed, calleesByFunction))
        {
        }

        if (sawMarker && suppressed.Count == 0)
        {
            suppressed.Add("*");
        }

        return suppressed;
    }

    // Reads the file line by line: records the callees of every defined function and the functions that a suppress
    // marker names. Returns whether a marker was seen.
    private static bool ScanFunctions(
        string cuContent,
        HashSet<string> suppressed,
        Dictionary<string, HashSet<string>> calleesByFunction)
    {
        var sawMarker = false;
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

            if (line.IndexOf(SuppressGs0001Marker, StringComparison.Ordinal) >= 0)
            {
                sawMarker = true;
                pendingSuppress = true;
                continue;
            }

            if (braceDepth == 0 && TryExtractDefinedFunctionName(line, out var fnName) &&
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
            else if (pendingSuppress && TryExtractDefinedFunctionName(line, out var markedFn))
            {
                suppressed.Add(markedFn);
                pendingSuppress = false;
            }

            if (currentFunction.Length > 0 && calleesByFunction.TryGetValue(currentFunction, out var callees))
            {
                CollectGSharpCallees(line, currentFunction, callees);
            }

            if (AdvanceBraces(line, ref braceDepth))
            {
                currentFunction = "";
            }
        }

        return sawMarker;
    }

    private static void RegisterFunction(string function, Dictionary<string, HashSet<string>> calleesByFunction)
    {
        if (!calleesByFunction.ContainsKey(function))
        {
            calleesByFunction[function] = new HashSet<string>(StringComparer.Ordinal);
        }
    }

    // Counts the braces of the line into the depth. Returns whether a closing brace brought the depth to zero.
    private static bool AdvanceBraces(string line, ref int braceDepth)
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
    // Returns whether the set grew.
    private static bool PropagateSuppression(
        HashSet<string> suppressed,
        Dictionary<string, HashSet<string>> calleesByFunction)
    {
        var changed = false;
        foreach (var kvp in calleesByFunction)
        {
            if (suppressed.Contains(kvp.Key))
            {
                foreach (var callee in kvp.Value)
                {
                    changed |= suppressed.Add(callee);
                }
            }
            else if (kvp.Key.StartsWith("__gsharp_k_", StringComparison.Ordinal) && kvp.Value.Overlaps(suppressed))
            {
                changed |= suppressed.Add(kvp.Key);
            }
        }

        return changed;
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

        if (end < 0)
        {
            return false;
        }

        var start = end;
        while (start >= 0 && (char.IsLetterOrDigit(line[start]) || line[start] == '_'))
        {
            start--;
        }

        start++;
        if (start > end)
        {
            return false;
        }

        var endExclusive = end + 1;
        var candidate = line[start..endExclusive];
        if (candidate is "if" or "for" or "while" or "switch" or "return" or "sizeof" or "alignof" or "defined" or "__declspec" or "__attribute__")
        {
            return false;
        }

        // Require a return type or qualifier before the function identifier
        var prefix = line.AsSpan(0, start).Trim();
        if (prefix.Length == 0)
        {
            return false;
        }

        functionName = candidate;
        return true;
    }

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
            // A call either is the same function inside its body (after the signature line) or another function followed by '(' or '<'
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
