// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class ParenthesesSuppressionKernels
{
    [GpuKernel]
    public static void ParenthesesKernel(int[] output, int a, int b, int c)
    {
        // Simple logical expression: if (a < b)
        if (a < b)
        {
            output[0] = 1;
        }
        else
        {
            output[0] = 0;
        }

        // Mixed operators: (a & b) == c
        if ((a & b) == c)
        {
            output[1] = 10;
        }

        // Chained arithmetic operations: a * (b + c)
        output[2] = a * (b + c);
    }
}
