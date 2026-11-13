using System.Runtime.CompilerServices;

namespace CallerInfoCases;

// Each element of Run is one call that omits a caller-info parameter; the test compares the value C# passes at run
// time with the value the compiler reads for the same call
public static class CallerInfoArgumentSource
{
    public static object?[] Run() =>
    [
        Line(),
        Line
        (
        ),
        WideLine(),
        NullableLine(),
        File(),
        Member(),
        Expression(1 + 2),
        Expression(value: Line()),
        Both(),
        new Marked().Path,
        new Marked(1).Line,
#line 700 "mapped/Other.cs"
        Line(),
        File(),
#line default
    ];

    public static int Explicit() => Line(42);

    public static int Line([CallerLineNumber] int line = 0) => line;

    public static long WideLine([CallerLineNumber] long line = 0) => line;

    public static int? NullableLine([CallerLineNumber] int? line = null) => line;

    public static string File([CallerFilePath] string path = "") => path;

    public static string Member([CallerMemberName] string name = "") => name;

    public static string? Expression(int value, [CallerArgumentExpression(nameof(value))] string? text = null) => text;

    // A line number wins over a file path
    public static object Both([CallerFilePath, CallerLineNumber] object? info = null) => info ?? "";
}

public sealed class Marked
{
    public readonly string Path;
    public readonly int Line;

    public Marked([CallerFilePath] string path = "")
    {
        Path = path;
        Line = 0;
    }

    public Marked(int value, [CallerLineNumber] int line = 0)
    {
        Path = "";
        Line = line + value - 1;
    }
}
