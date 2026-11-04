using Kassyi.GSharp.Core;

[assembly: BclIntrinsicMap("Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.BclIntrinsicMap.MockBclWithOptional", "Compute", ["System.Int32", "System.Int32", "System.Int32"], "mock_compute")]

namespace Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.BclIntrinsicMap;

public static class MockBclWithOptional
{
    public static int Compute(int a, int b = 42, int c = 99) => a + b + c;
}

public static class MockBclCaller
{
    public static void CallWithOmittedMiddle(int x, int z)
    {
        _ = MockBclWithOptional.Compute(x, c: z);
    }

    public static void CallWithOmittedTrailing(int x, int y)
    {
        _ = MockBclWithOptional.Compute(x, y);
    }
}
