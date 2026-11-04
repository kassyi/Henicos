#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Diagnostics;

public static partial class MultidimensionalParameterDiagnostics
{
    [GpuKernel]
    public static void MatrixParamKernel(int[,] grid, int[] output)
    {
        output[0] = grid[0, 1];
    }
}
