namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// Structs that hold the same struct more than once without a cycle
public struct Pair
{
    public long A;
    public long B;
}

public struct Wrapper
{
    public int X;
    public Pair Held;
}

public struct Diamond
{
    public Pair Left;
    public Pair Right;
    public Wrapper Wrapped;
}
