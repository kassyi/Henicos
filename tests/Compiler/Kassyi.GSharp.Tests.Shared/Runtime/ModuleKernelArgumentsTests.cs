#nullable enable
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// <see cref="ModuleKernelArguments.Flatten{T}"/>: the elements of a multidimensional array as one span in row-major
/// order, the order in which a module launcher copies them to the kernel and back. No launch runs here.
/// </summary>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ModuleKernelArgumentsTests
{
    // Element [i, j] of a 3 x 5 array is 100 * i + j; row-major order puts [0, 4] before [1, 0], so a column-major or
    // transposed span differs at index 1 already
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Flatten_TwoDimensionalArray_ListsElementsRowByRow()
    {
        var grid = new float[3, 5];
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 5; j++)
            {
                grid[i, j] = 100 * i + j;
            }
        }

        var span = ModuleKernelArguments.Flatten<float>(grid);

        Assert.Equal(15, span.Length);
        for (var k = 0; k < span.Length; k++)
        {
            Assert.Equal(100 * (k / 5) + k % 5, span[k]);
        }
    }

    // The span is the array's own storage: the launcher copies the kernel's results back through it
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Flatten_WriteThroughSpan_ChangesTheArray()
    {
        var cube = new int[2, 3, 4];

        var span = ModuleKernelArguments.Flatten<int>(cube);
        span[(1 * 3 + 2) * 4 + 3] = 42;

        Assert.Equal(42, cube[1, 2, 3]);
        Assert.Equal(24, span.Length);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Flatten_NullOrEmptyArray_GivesEmptySpan()
    {
        Assert.True(ModuleKernelArguments.Flatten<float>(null).IsEmpty);
        Assert.True(ModuleKernelArguments.Flatten<float>(new float[0, 7]).IsEmpty);
    }

    // The generator passes the element type of the parameter; any other type would read the bytes of the elements as
    // values of another size
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Flatten_OtherElementType_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => ModuleKernelArguments.Flatten<int>(new float[2, 2]));

        Assert.Equal("array", exception.ParamName);
    }
}
