namespace Kassyi.GSharp.Tests.Shared.TestCases.Extensions;

public static class TreeZeroConsumer
{
    public static int Consume(int value)
    {
        int localZero = value + 1;
        return localZero;
    }
}
