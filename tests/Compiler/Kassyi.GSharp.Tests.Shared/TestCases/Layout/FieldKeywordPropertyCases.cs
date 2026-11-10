namespace Kassyi.GSharp.Tests.Shared.TestCases.Layout;

// Properties with a backing field: only Auto and AutoInit are auto-properties; the others have an accessor of their
// own that uses the field keyword, so the device calls their accessors
public partial struct FieldKeywordLayout
{
    public byte A;

    public int Auto { get; set; }

    public int AutoInit { get; init; }

    public long SetterOnly { get; set => field = value * 2; }

    public int GetterOnly { get => field + 1; set; }

    public short Both { get => (short)(field * 2); set => field = value; }

    public int InitOnly { get; init => field = value * 3; }

    public int ReadOnly { get => field * 3; }

    public int Computed => A + Auto;

    public partial int Partial { get; set; }
}

public partial struct FieldKeywordLayout
{
    public partial int Partial { get => field; set => field = value; }
}

// The properties of positional record parameters are auto-properties; a redeclared one that uses field is not
public record struct FieldKeywordRecord(int X, int Y)
{
    public int Y { get; init => field = value * 2; } = Y;
}
