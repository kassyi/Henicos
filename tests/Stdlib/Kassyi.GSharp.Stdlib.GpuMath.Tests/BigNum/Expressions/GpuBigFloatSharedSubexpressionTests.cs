#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// A float vector sub-expression read twice at different offsets: its second evaluation reuses the buffers of the
/// first, and both reads still see their own window.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class GpuBigFloatSharedSubexpressionTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;
    private readonly List<IDisposable> _owned = [];

    public GpuBigFloatSharedSubexpressionTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1024 * 1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        foreach (var owned in _owned)
        {
            owned.Dispose();
        }

        _workspace.Dispose();
        _scope.Dispose();
    }

    // e is read at offset 1 (Skip) and at offset 0 (Take): its second evaluation reuses the buffers of the first, so
    // the node keeps the right result and evaluates the left one again.
    // mutant: VectorOperandPair.Evaluate returning before the check ([0, 0, 0] and all false)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigFloatArray_SharedSubexpression_AtTwoOffsets_KeepsBothResults()
    {
        var e = TimesThree([1, 2, 4, 8]);
        using var difference = e.Skip(1).ZipSubtract(e.Take(3));
        using var greater = e.Skip(1).IsGreaterThan(e.Take(3));

        var result = difference.Evaluate(_workspace);
        var flags = greater.Evaluate(_workspace);
        var flagsHost = new uint[3];
        _workspace.Provider.CopyDeviceToHost(flagsHost, flags.DevicePointer, 3);

        AssertElement(result.GetElement(0), 3, "element 0");
        AssertElement(result.GetElement(1), 6, "element 1");
        AssertElement(result.GetElement(2), 12, "element 2");
        uint[] expectedFlags = [1, 1, 1];
        Assert.Equal(expectedFlags, flagsHost);
    }

    // The source reads e at offset 1 and the predicate at offset 0: source [6, 12, 24], predicate [3, 6, 12] > 5 =
    // [F, T, T], so the selection is [12, 24].
    // mutant: FloatVectorWhereExpression.EvaluateCore evaluating the source before the predicate ([6, 12])
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigFloatArray_SharedSubexpression_InTheSourceAndThePredicateOfWhere_KeepsBothResults()
    {
        var e = TimesThree([1, 2, 4, 8]);
        using var five = CreateFloat(5);
        using var selected = e.Skip(1).Where(e.Take(3).IsGreaterThan(five));

        var result = selected.Evaluate(_workspace);

        Assert.Equal(2, result.Length);
        AssertElement(result.GetElement(0), 12, "element 0");
        AssertElement(result.GetElement(1), 24, "element 1");
    }

    // [values] * 3 as a vector expression over an array this test owns
    private IGpuBigFloatVectorExpression TimesThree(long[] values)
    {
        var array = new GpuBigFloatArray(_workspace, values.Length, 4);
        _owned.Add(array);
        for (var i = 0; i < values.Length; i++)
        {
            using var element = CreateFloat(values[i]);
            array.SetElement(i, element);
        }

        var three = CreateFloat(3);
        _owned.Add(three);
        return array.AsVectorizedEnumerable().SelectMultiply(three);
    }

    private GpuBigFloat CreateFloat(long value)
    {
        var words = new uint[4];
        var magnitude = (ulong)Math.Abs(value);
        words[0] = (uint)magnitude;
        words[1] = (uint)(magnitude >> 32);
        return new GpuBigFloat(new GpuBigUInt(_workspace, words), 0, value < 0);
    }

    private static void AssertElement(scoped GpuBigFloatView element, BigInteger expected, string label)
    {
        var significand = element.Significand.ToBigInteger();
        Assert.True(
            BigFloatReference.EqualsRational(significand, element.Exponent, element.IsNegative, expected, 1),
            $"{label}: expected {expected}, actual "
            + BigFloatReference.Describe(significand, element.Exponent, element.IsNegative));
    }
}
