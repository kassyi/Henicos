using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN91_92_InvalidParenthesesSadPathCase
{
    [GpuKernel]
    public static void KernelWithUnsupportedSyntaxInParentheses(int[] data)
    {
        // Sad Path: Unsupported syntax wrapped inside parentheses
        var obj = (new object());
        data[0] = 1;
    }
}
