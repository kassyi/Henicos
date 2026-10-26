namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Shape, element views and slices of <see cref="GpuBigUIntArray"/> and <see cref="GpuBigIntArray"/>.
/// </summary>
/// <remarks>
/// Element views and slices are not disposed: disposing one frees the parent's buffer (#630).
/// </remarks>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class GpuBigUIntArrayTests : IDisposable
{
    private const int Length = 3;
    private const int WordsPerElement = 4;
    private const int ElementBytes = WordsPerElement * sizeof(uint);

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBigUIntArrayTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Properties_ReflectShape()
    {
        // The array owns one buffer of Length * WordsPerElement words.
        // mutant: GpuBigUIntArray.cs:39 `Length * WordsPerElement` -> `Length / WordsPerElement`
        // mutant: GpuBigUIntArray.cs:67 `IsTemp = false` -> `IsTemp = true`
        using var array = new GpuBigUIntArray(_workspace, Length, WordsPerElement);
        Assert.Equal(
            (Length, WordsPerElement, Length * WordsPerElement, Length * WordsPerElement, false),
            (array.Length, array.WordsPerElement, array.TotalWords, array.Handle.NumWords, array.IsTemp)
        );
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(-1)]
    [InlineData(Length)]
    public void GetElement_OutOfRange_Throws(int index)
    {
        // Only 0 <= index < Length names an element.
        // mutant: GpuBigUIntArray.cs:77 `index < 0` -> `index <= 0` / `index >= Length` -> `index > Length` / `||` -> `&&`
        using var array = new GpuBigUIntArray(_workspace, Length, WordsPerElement);
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => array.GetElement(index));
        Assert.Equal("index", error.ParamName);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(Length - 1)]
    public void GetElement_InRange_ViewsTheElementWords(int index)
    {
        // Element i is the WordsPerElement words starting at word i * WordsPerElement of the array.
        // mutant: GpuBigUIntArray.cs:77 `index < 0` -> `index <= 0` (index 0)
        using var array = new GpuBigUIntArray(_workspace, Length, WordsPerElement);
        var element = array.GetElement(index);
        Assert.Equal(
            (index, array.DevicePointer + (index * ElementBytes), WordsPerElement),
            (index, element.DevicePointer, element.NumWords)
        );
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(3, 1)]
    [InlineData(1, 3)]
    public void Slice_OutOfRange_Throws(int offset, int count)
    {
        // A slice must lie inside 0..Length.
        // mutant: GpuBigUIntArray.cs:95 each comparison and `||` (one row per condition)
        using var array = new GpuBigUIntArray(_workspace, Length, WordsPerElement);
        Assert.Throws<ArgumentOutOfRangeException>(() => array.Slice(offset, count));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(1, 2)]
    [InlineData(0, 3)]
    public void Slice_InRange_ViewsTheElements(int offset, int count)
    {
        // A slice of `count` elements at `offset` starts offset * WordsPerElement words in and spans count elements.
        // mutant: GpuBigUIntArray.cs:95 `>` -> `>=` (rows that end at Length)
        // mutant: GpuBigUIntArray.cs:101 `elementCount * WordsPerElement` -> `elementCount / WordsPerElement`
        using var array = new GpuBigUIntArray(_workspace, Length, WordsPerElement);
        var slice = array.Slice(offset, count);
        Assert.Equal(
            (array.DevicePointer + (offset * ElementBytes), count, count * WordsPerElement, WordsPerElement),
            (slice.DevicePointer, slice.Length, slice.Handle.NumWords, slice.WordsPerElement)
        );
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SliceOfSlice_AndElementOfSlice_AddTheOffsets()
    {
        // Offsets add up: a slice of a slice and an element of a slice start after the outer slice's offset.
        // mutant: GpuBigUIntArray.cs:100 `Handle.WordOffset + ...` -> `Handle.WordOffset - ...`
        // mutant: GpuBigUIntArray.cs:82 `Handle.WordOffset + ...` -> `Handle.WordOffset - ...`
        using var array = new GpuBigUIntArray(_workspace, Length, WordsPerElement);
        var outer = array.Slice(1, 2);
        Assert.Equal(array.DevicePointer + (2 * ElementBytes), outer.Slice(1, 1).DevicePointer);
        Assert.Equal(array.DevicePointer + (2 * ElementBytes), outer.GetElement(1).DevicePointer);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void GpuBigIntArray_MismatchedLengths_Throws()
    {
        // The values and the signs of a signed array have one entry per element.
        // mutant: GpuBigIntArray.cs:50 `values.Length != signs.Length` -> `==` / throw removed
        using var values = new GpuBigUIntArray(_workspace, 3, 2);
        using var signs = new GpuBigUIntArray(_workspace, 2, 1);
        Assert.Throws<ArgumentException>(() => new GpuBigIntArray(values, signs));

        // Matching lengths are accepted.
        using var matchingSigns = new GpuBigUIntArray(_workspace, 3, 1);
        var signed = new GpuBigIntArray(values, matchingSigns);
        Assert.Equal((3, 2), (signed.Length, signed.WordsPerElement));
    }
}
