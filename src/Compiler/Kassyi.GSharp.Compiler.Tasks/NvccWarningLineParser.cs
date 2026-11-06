using System.Text;

namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>Parses one nvcc, ptxas, MSVC or GCC / Clang warning line into its file, location, code and message.</summary>
internal static class NvccWarningLineParser
{
    internal static bool TryParseWarningLine(string line, out NativeCompilerWarning warning)
    {
        warning = default;

        // 1. ptxas warning : <msg>
        if (line.StartsWith("ptxas warning", StringComparison.OrdinalIgnoreCase))
        {
            var colonIdx = line.IndexOf(':');
            var colonNext = colonIdx + 1;
            var msg = colonIdx >= 0 ? line.AsSpan(colonNext).Trim().ToString() : line;
            warning = new NativeCompilerWarning(null, 0, 0, "PTXAS", $"{line}", line);
            return msg.Length > 0;
        }

        // 2. EDG / MSVC format with (line) or (line,col): <file>(<line>[,<col>]): warning <code>: <msg>
        var warnMarkerIdx = line.IndexOf(": warning ", StringComparison.OrdinalIgnoreCase);
        if (warnMarkerIdx > 0)
        {
            var locationPart = line.AsSpan(0, warnMarkerIdx).Trim().ToString();
            var restStart = warnMarkerIdx + ": warning ".Length;
            var rest = line.AsSpan(restStart).Trim().ToString();

            if (TryParseParenLocation(locationPart, out var file, out var lineNum, out var colNum))
            {
                var colonIdx = rest.IndexOf(':');
                if (colonIdx > 0)
                {
                    var rawCode = rest.AsSpan(0, colonIdx).Trim().ToString();
                    var code = rawCode.StartsWith("#", StringComparison.Ordinal) ? rawCode[1..] : rawCode;
                    var msgStart = colonIdx + 1;
                    var msgText = rest.AsSpan(msgStart).Trim().ToString();
                    warning = new NativeCompilerWarning(file, lineNum, colNum, code, $"warning {rawCode}: {msgText}", line);
                    return true;
                }

                warning = new NativeCompilerWarning(file, lineNum, colNum, null, $"warning: {rest}", line);
                return true;
            }

            // 3. GCC / Clang format: <file>:<line>:<col>: warning: <msg>
            if (TryParseColonLocation(locationPart, out var gccFile, out var gccLine, out var gccCol))
            {
                var msg = rest.StartsWith(":", StringComparison.Ordinal) ? rest.AsSpan(1).Trim().ToString() : rest;
                warning = new NativeCompilerWarning(gccFile, gccLine, gccCol, null, $"warning: {msg}", line);
                return true;
            }
        }

        // 4. GCC / Clang format where ": warning:" has no space before colon: <file>:<line>:<col>: warning: <msg>
        var gccMarkerIdx = line.IndexOf(": warning:", StringComparison.OrdinalIgnoreCase);
        if (gccMarkerIdx > 0)
        {
            var locationPart = line.AsSpan(0, gccMarkerIdx).Trim().ToString();
            var msgStart = gccMarkerIdx + ": warning:".Length;
            var msg = line.AsSpan(msgStart).Trim().ToString();
            if (TryParseColonLocation(locationPart, out var gccFile, out var gccLine, out var gccCol))
            {
                warning = new NativeCompilerWarning(gccFile, gccLine, gccCol, null, $"warning: {msg}", line);
                return true;
            }

            warning = new NativeCompilerWarning(locationPart, 0, 0, null, $"warning: {msg}", line);
            return true;
        }

        return false;
    }

    private static bool TryParseParenLocation(string locationPart, out string file, out int lineNum, out int colNum)
    {
        file = "";
        lineNum = 0;
        colNum = 0;

        if (!locationPart.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        var openParen = locationPart.LastIndexOf('(');
        if (openParen <= 0)
        {
            return false;
        }

        file = locationPart.AsSpan(0, openParen).Trim().ToString();
        var insideStart = openParen + 1;
        var insideEnd = locationPart.Length - 1;
        var inside = locationPart[insideStart..insideEnd];
        var commaIdx = inside.IndexOf(',');
        if (commaIdx > 0)
        {
            var lineText = inside.AsSpan(0, commaIdx).Trim().ToString();
            var colStart = commaIdx + 1;
            var colText = inside.AsSpan(colStart).Trim().ToString();
            int.TryParse(lineText, out lineNum);
            int.TryParse(colText, out colNum);
            return lineNum > 0;
        }

        return int.TryParse(inside.AsSpan().Trim().ToString(), out lineNum) && lineNum > 0;
    }

    private static bool TryParseColonLocation(string locationPart, out string file, out int lineNum, out int colNum)
    {
        file = locationPart;
        lineNum = 0;
        colNum = 0;

        var lastColon = locationPart.LastIndexOf(':');
        if (lastColon <= 1)
        {
            return false;
        }

        var lastStart = lastColon + 1;
        var lastText = locationPart.AsSpan(lastStart).Trim().ToString();
        if (!int.TryParse(lastText, out var lastNum))
        {
            return false;
        }

        var prevColon = locationPart.LastIndexOf(':', lastColon - 1);
        if (prevColon > 1)
        {
            var firstStart = prevColon + 1;
            var firstLen = lastColon - prevColon - 1;
            var firstText = locationPart.AsSpan(firstStart, firstLen).Trim().ToString();
            if (int.TryParse(firstText, out var firstNum))
            {
                file = locationPart[..prevColon];
                lineNum = firstNum;
                colNum = lastNum;
                return true;
            }
        }

        file = locationPart[..lastColon];
        lineNum = lastNum;
        return true;
    }
}
