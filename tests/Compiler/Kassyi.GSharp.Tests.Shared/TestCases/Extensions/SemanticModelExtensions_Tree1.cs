namespace Kassyi.GSharp.Tests.Shared.TestCases.Extensions;

public static class TreeOneProvider
{
    public const int Step = 7;

    public static int Helper(int x) => x * Step;

    public static int Compute(int input)
    {
        int factor = Helper(input);
        return factor + Step;
    }
}
