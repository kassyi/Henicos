using System.Text;

namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>
/// Parses one nvcc, ptxas, MSVC or GCC / Clang warning line into its file, location, code and message.
/// </summary>
internal static class NvccWarningLineParser
{
    // Tries the formats in order; the first whose marker is in the line decides, except that a failed EDG / MSVC or
    // GCC / Clang match with a space before the colon falls through to the tight form.
    internal static bool TryParseWarningLine(string line, out NativeCompilerWarning warning)
    {
        if (line.StartsWith("ptxas warning", StringComparison.OrdinalIgnoreCase))
        {
            return TryParsePtxasWarning(line, out warning);
        }

        return TryParseParenOrSpacedColonWarning(line, out warning) || TryParseTightColonWarning(line, out warning);
    }

    // ptxas warning : <msg>
    private static bool TryParsePtxasWarning(string line, out NativeCompilerWarning warning)
    {
        var colonIdx = line.IndexOf(':');
        var hasMessage = colonIdx < 0 || line.AsSpan(colonIdx + 1).Trim().Length > 0;
        warning = new NativeCompilerWarning(null, 0, 0, "PTXAS", line, line);
        return hasMessage;
    }

    // EDG / MSVC: <file>(<line>[,<col>]): warning <code>: <msg>
    // GCC / Clang: <file>:<line>:<col>: warning: <msg> (written with a space before the colon of "warning")
    private static bool TryParseParenOrSpacedColonWarning(string line, out NativeCompilerWarning warning)
    {
        warning = default;
        var warnMarkerIdx = line.IndexOf(": warning ", StringComparison.OrdinalIgnoreCase);
        if (warnMarkerIdx <= 0)
        {
            return false;
        }

        var locationPart = line.AsSpan(0, warnMarkerIdx).Trim().ToString();
        var restStart = warnMarkerIdx + ": warning ".Length;
        var rest = line.AsSpan(restStart).Trim().ToString();

        if (TryParseParenLocation(locationPart, out var file, out var lineNum, out var colNum))
        {
            warning = CreateParenWarning(file, lineNum, colNum, rest, line);
            return true;
        }

        if (TryParseColonLocation(locationPart, out var gccFile, out var gccLine, out var gccCol))
        {
            var msg = rest.StartsWith(":", StringComparison.Ordinal) ? rest.AsSpan(1).Trim().ToString() : rest;
            warning = new NativeCompilerWarning(gccFile, gccLine, gccCol, null, $"warning: {msg}", line);
            return true;
        }

        return false;
    }

    // rest is "<code>: <msg>", or a bare message when it has no colon.
    private static NativeCompilerWarning CreateParenWarning(
        string file,
        int lineNum,
        int colNum,
        string rest,
        string line)
    {
        var colonIdx = rest.IndexOf(':');
        if (colonIdx <= 0)
        {
            return new NativeCompilerWarning(file, lineNum, colNum, null, $"warning: {rest}", line);
        }

        var rawCode = rest.AsSpan(0, colonIdx).Trim().ToString();
        var code = rawCode.StartsWith("#", StringComparison.Ordinal) ? rawCode[1..] : rawCode;
        var msgStart = colonIdx + 1;
        var msgText = rest.AsSpan(msgStart).Trim().ToString();
        return new NativeCompilerWarning(file, lineNum, colNum, code, $"warning {rawCode}: {msgText}", line);
    }

    // GCC / Clang where ": warning:" has no space before the colon: <file>:<line>:<col>: warning: <msg>
    private static bool TryParseTightColonWarning(string line, out NativeCompilerWarning warning)
    {
        warning = default;
        var gccMarkerIdx = line.IndexOf(": warning:", StringComparison.OrdinalIgnoreCase);
        if (gccMarkerIdx <= 0)
        {
            return false;
        }

        var locationPart = line.AsSpan(0, gccMarkerIdx).Trim().ToString();
        var msgStart = gccMarkerIdx + ": warning:".Length;
        var msg = line.AsSpan(msgStart).Trim().ToString();
        warning = TryParseColonLocation(locationPart, out var gccFile, out var gccLine, out var gccCol)
            ? new NativeCompilerWarning(gccFile, gccLine, gccCol, null, $"warning: {msg}", line)
            : new NativeCompilerWarning(locationPart, 0, 0, null, $"warning: {msg}", line);
        return true;
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
