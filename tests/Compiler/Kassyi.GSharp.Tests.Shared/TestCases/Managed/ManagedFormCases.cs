using System.Runtime.CompilerServices;

namespace ManagedFormCases;

public struct Cell
{
    public int Value;
}

public static class Forms
{
    // Caller-info and plain optional parameters
    public static int Line([CallerLineNumber] int line = 0) => line;
    public static int Plain(int value = 0) => value;

    // A ref readonly parameter
    public static int ReadOnly(ref readonly Cell cell) => cell.Value;
}

public static class CellExtensions
{
    public static int ByRef(this ref Cell cell) => cell.Value;
    public static int ByIn(this in Cell cell) => cell.Value;
    public static int ByReadOnly(this ref readonly Cell cell) => cell.Value;
    public static int ByValue(this Cell cell) => cell.Value;
}
