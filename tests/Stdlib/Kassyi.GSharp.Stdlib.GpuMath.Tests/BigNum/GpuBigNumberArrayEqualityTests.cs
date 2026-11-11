#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Handle-identity equality and hash codes for <see cref="GpuBigUIntArray"/>, <see cref="GpuBigIntArray"/>, and
/// <see cref="GpuBigFloatArray"/> without reflection through <see cref="ValueType.Equals(object?)"/>.
/// </summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class GpuBigNumberArrayEqualityTests
{
    private const int Length = 3;
    private const int WordsPerElement = 4;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigUIntArray_SameAllocationAndBorrowedViews_AreEqualWithMatchingHashCode()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(64);
        using var array = new GpuBigUIntArray(workspace, Length, WordsPerElement);
        using var other = new GpuBigUIntArray(workspace, Length, WordsPerElement);
        var copy = array;
        var borrowed = array.AsBorrowed();
        var wholeSlice = array.BorrowSlice(0, Length);

        // Value copies and borrowed views of the same range share workspace, handle lease, and shape.
        // mutant: GpuBigUIntArray.Equals compares OwnsBuffer (array.Equals(borrowed) becomes false)
        AssertEqualAndConsistentHash(array, copy);
        AssertEqualAndConsistentHash(array, borrowed);
        AssertEqualAndConsistentHash(array, wholeSlice);

        // HashSet deduplicates copies and full-range views while keeping a separate allocation distinct.
        var set = new HashSet<GpuBigUIntArray> { array, copy, borrowed, wholeSlice, other };
        Assert.Equal(2, set.Count);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigUIntArray_DefaultAndDisposedInstances_CompareWithoutReadingDevice()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(64);
        using var emptyAllocated = new GpuBigUIntArray(workspace, 0, 0);
        GpuBigUIntArray defaultFirst = default;
        GpuBigUIntArray defaultSecond = default;

        // Default instances have null workspace and default handle; both Equals and GetHashCode handle null safely.
        // mutant: GpuBigUIntArray.GetHashCode calls Workspace.GetHashCode() directly (throws NullReferenceException)
        AssertEqualAndConsistentHash(defaultFirst, defaultSecond);
        AssertNotEqual(defaultFirst, emptyAllocated);

        // Disposing advances the buffer generation; stale copies still agree with each other without touching device
        // memory, and do not match a subsequent allocation that reuses the pooled buffer.
        var freed = new GpuBigUIntArray(workspace, 7, 1);
        var staleCopy = freed;
        freed.Dispose();
        using var reused = new GpuBigUIntArray(workspace, 7, 1);

        Assert.Throws<ObjectDisposedException>(() => _ = freed.DevicePointer);
        AssertEqualAndConsistentHash(freed, staleCopy);
        AssertNotEqual(freed, reused);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigUIntArray_DistinctBufferSliceWorkspaceOrShape_AreNotEqual()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspaceA = new GpuWorkspace(64);
        using var workspaceB = new GpuWorkspace(64);
        using var array = new GpuBigUIntArray(workspaceA, Length, WordsPerElement);
        using var separateBuffer = new GpuBigUIntArray(workspaceA, Length, WordsPerElement);

        // Separate buffer of identical shape differs in Handle.
        // mutant: GpuBigUIntArray.Equals drops `Handle.Equals(other.Handle)`
        AssertNotEqual(array, separateBuffer);
        AssertNotEqual(array, array.BorrowSlice(0, Length - 1));
        AssertNotEqual(array, array.BorrowSlice(1, Length - 1));

        // Zero-word arrays share the default handle, isolating Workspace, Length, and WordsPerElement checks.
        // mutant: GpuBigUIntArray.Equals drops `ReferenceEquals(Workspace, other.Workspace)`
        using var emptyWsA = new GpuBigUIntArray(workspaceA, 0, WordsPerElement);
        using var emptyWsB = new GpuBigUIntArray(workspaceB, 0, WordsPerElement);
        AssertNotEqual(emptyWsA, emptyWsB);

        // mutant: GpuBigUIntArray.Equals drops `Length == other.Length` or GetHashCode omits Length
        using var zeroWordLen3 = new GpuBigUIntArray(workspaceA, 3, 0);
        using var zeroWordLen5 = new GpuBigUIntArray(workspaceA, 5, 0);
        AssertNotEqual(zeroWordLen3, zeroWordLen5);
        Assert.NotEqual(zeroWordLen3.GetHashCode(), zeroWordLen5.GetHashCode());

        // mutant: GpuBigUIntArray.Equals drops `WordsPerElement == other.WordsPerElement` or GetHashCode omits it
        using var zeroLenWords2 = new GpuBigUIntArray(workspaceA, 0, 2);
        using var zeroLenWords4 = new GpuBigUIntArray(workspaceA, 0, 4);
        AssertNotEqual(zeroLenWords2, zeroLenWords4);
        Assert.NotEqual(zeroLenWords2.GetHashCode(), zeroLenWords4.GetHashCode());

        // Same 12-word buffer reshaped as (3, 4) vs (4, 3) differs in element shape.
        var reshaped = new GpuBigUIntArray(workspaceA, array.Handle.Borrow(), WordsPerElement, Length);
        AssertNotEqual(array, reshaped);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GpuBigUIntArray_NullOrOtherObjectType_ReturnsFalse()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(64);
        using var uintArray = new GpuBigUIntArray(workspace, Length, WordsPerElement);
        using var intArray = new GpuBigIntArray(workspace, Length, WordsPerElement);
        using var floatArray = new GpuBigFloatArray(workspace, Length, WordsPerElement);

        // Object overload rejects null, unrelated objects, and other GPU array types.
        // mutant: GpuBigUIntArray.Equals(object?) returns true for non-GpuBigUIntArray
        Assert.False(uintArray.Equals(null));
        Assert.False(default(GpuBigUIntArray).Equals(null));
        Assert.False(uintArray.Equals("not an array"));
        Assert.False(uintArray.Equals((object)intArray));
        Assert.False(uintArray.Equals((object)floatArray));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigIntArray_SameAllocationAndBorrowedSlice_AreEqualWithMatchingHashCode()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(64);
        using var array = new GpuBigIntArray(workspace, Length, WordsPerElement);
        using var other = new GpuBigIntArray(workspace, Length, WordsPerElement);
        var copy = array;
        var wholeSlice = array.BorrowSlice(0, Length);

        // Copies and full-range borrowed slices share both Values and Signs handles.
        AssertEqualAndConsistentHash(array, copy);
        AssertEqualAndConsistentHash(array, wholeSlice);

        var set = new HashSet<GpuBigIntArray> { array, copy, wholeSlice, other };
        Assert.Equal(2, set.Count);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigIntArray_DefaultAndDisposedInstances_CompareWithoutReadingDevice()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(64);
        using var emptyAllocated = new GpuBigIntArray(workspace, 0, WordsPerElement);
        GpuBigIntArray defaultFirst = default;
        GpuBigIntArray defaultSecond = default;

        // Default instances compare equal and hash without throwing; allocated empty arrays differ via Workspace.
        AssertEqualAndConsistentHash(defaultFirst, defaultSecond);
        AssertNotEqual(defaultFirst, emptyAllocated);

        var freed = new GpuBigIntArray(workspace, 5, 2);
        var staleCopy = freed;
        freed.Dispose();
        using var reused = new GpuBigIntArray(workspace, 5, 2);

        AssertEqualAndConsistentHash(freed, staleCopy);
        AssertNotEqual(freed, reused);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigIntArray_DifferingValuesSignsSliceOrShape_AreNotEqual()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(64);
        using var array = new GpuBigIntArray(workspace, Length, WordsPerElement);
        using var separate = new GpuBigIntArray(workspace, Length, WordsPerElement);
        using var differentLength = new GpuBigIntArray(workspace, Length + 1, WordsPerElement);
        using var differentWidth = new GpuBigIntArray(workspace, Length, WordsPerElement + 1);

        AssertNotEqual(array, separate);
        AssertNotEqual(array, array.BorrowSlice(1, Length - 1));
        AssertNotEqual(array, differentLength);
        AssertNotEqual(array, differentWidth);
        Assert.NotEqual(array.GetHashCode(), differentWidth.GetHashCode());

        // Differing only in Values while sharing Signs must not compare equal.
        // mutant: GpuBigIntArray.Equals drops `Values.Equals(other.Values)`
        using var altValues = new GpuBigUIntArray(workspace, Length, WordsPerElement);
        var onlyValuesDiffer = new GpuBigIntArray(altValues.AsBorrowed(), array.Signs.AsBorrowed());
        AssertNotEqual(array, onlyValuesDiffer);

        // Differing only in Signs while sharing Values must not compare equal.
        // mutant: GpuBigIntArray.Equals drops `Signs.Equals(other.Signs)`
        using var altSigns = new GpuBigUIntArray(workspace, Length, 1);
        var onlySignsDiffer = new GpuBigIntArray(array.Values.AsBorrowed(), altSigns.AsBorrowed());
        AssertNotEqual(array, onlySignsDiffer);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GpuBigIntArray_NullOrOtherObjectType_ReturnsFalse()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(64);
        using var uintArray = new GpuBigUIntArray(workspace, Length, WordsPerElement);
        using var intArray = new GpuBigIntArray(workspace, Length, WordsPerElement);
        using var floatArray = new GpuBigFloatArray(workspace, Length, WordsPerElement);

        // mutant: GpuBigIntArray.Equals(object?) returns true for non-GpuBigIntArray
        Assert.False(intArray.Equals(null));
        Assert.False(default(GpuBigIntArray).Equals(null));
        Assert.False(intArray.Equals("not an array"));
        Assert.False(intArray.Equals((object)uintArray));
        Assert.False(intArray.Equals((object)floatArray));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigFloatArray_SameAllocationAndBorrowedSlice_AreEqualWithMatchingHashCode()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(64);
        using var array = new GpuBigFloatArray(workspace, Length, WordsPerElement);
        using var other = new GpuBigFloatArray(workspace, Length, WordsPerElement);
        var copy = array;
        var wholeSlice = array.BorrowSlice(0, Length);

        // Copies and full-range borrowed slices share Significands, Exponents, and Signs handles.
        AssertEqualAndConsistentHash(array, copy);
        AssertEqualAndConsistentHash(array, wholeSlice);

        var set = new HashSet<GpuBigFloatArray> { array, copy, wholeSlice, other };
        Assert.Equal(2, set.Count);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigFloatArray_DefaultAndDisposedInstances_CompareWithoutReadingDevice()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(64);
        using var emptyAllocated = new GpuBigFloatArray(workspace, 0, WordsPerElement);
        GpuBigFloatArray defaultFirst = default;
        GpuBigFloatArray defaultSecond = default;

        AssertEqualAndConsistentHash(defaultFirst, defaultSecond);
        AssertNotEqual(defaultFirst, emptyAllocated);

        var freed = new GpuBigFloatArray(workspace, 5, 2);
        var staleCopy = freed;
        freed.Dispose();
        using var reused = new GpuBigFloatArray(workspace, 5, 2);

        AssertEqualAndConsistentHash(freed, staleCopy);
        AssertNotEqual(freed, reused);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigFloatArray_DifferingComponentBufferSliceOrShape_AreNotEqual()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(64);
        using var array = new GpuBigFloatArray(workspace, Length, WordsPerElement);
        using var separate = new GpuBigFloatArray(workspace, Length, WordsPerElement);
        using var differentLength = new GpuBigFloatArray(workspace, Length + 1, WordsPerElement);
        using var differentWidth = new GpuBigFloatArray(workspace, Length, WordsPerElement + 1);

        AssertNotEqual(array, separate);
        AssertNotEqual(array, array.BorrowSlice(1, Length - 1));
        AssertNotEqual(array, differentLength);
        AssertNotEqual(array, differentWidth);
        Assert.NotEqual(array.GetHashCode(), differentWidth.GetHashCode());

        // Differing only in Significands while sharing Exponents and Signs must not compare equal.
        // mutant: GpuBigFloatArray.Equals drops `Significands.Equals(other.Significands)`
        using var altSignificands = new GpuBigUIntArray(workspace, Length, WordsPerElement);
        var onlySignificandsDiffer = new GpuBigFloatArray(
            altSignificands.AsBorrowed(), array.Exponents.AsBorrowed(), array.Signs.AsBorrowed());
        AssertNotEqual(array, onlySignificandsDiffer);

        // Differing only in Exponents while sharing Significands and Signs must not compare equal.
        // mutant: GpuBigFloatArray.Equals drops `Exponents.Equals(other.Exponents)`
        using var altExponents = new GpuBigUIntArray(workspace, Length, 1);
        var onlyExponentsDiffer = new GpuBigFloatArray(
            array.Significands.AsBorrowed(), altExponents.AsBorrowed(), array.Signs.AsBorrowed());
        AssertNotEqual(array, onlyExponentsDiffer);

        // Differing only in Signs while sharing Significands and Exponents must not compare equal.
        // mutant: GpuBigFloatArray.Equals drops `Signs.Equals(other.Signs)`
        using var altSigns = new GpuBigUIntArray(workspace, Length, 1);
        var onlySignsDiffer = new GpuBigFloatArray(
            array.Significands.AsBorrowed(), array.Exponents.AsBorrowed(), altSigns.AsBorrowed());
        AssertNotEqual(array, onlySignsDiffer);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GpuBigFloatArray_NullOrOtherObjectType_ReturnsFalse()
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(64);
        using var uintArray = new GpuBigUIntArray(workspace, Length, WordsPerElement);
        using var intArray = new GpuBigIntArray(workspace, Length, WordsPerElement);
        using var floatArray = new GpuBigFloatArray(workspace, Length, WordsPerElement);

        // mutant: GpuBigFloatArray.Equals(object?) returns true for non-GpuBigFloatArray
        Assert.False(floatArray.Equals(null));
        Assert.False(default(GpuBigFloatArray).Equals(null));
        Assert.False(floatArray.Equals("not an array"));
        Assert.False(floatArray.Equals((object)uintArray));
        Assert.False(floatArray.Equals((object)intArray));
    }

    // Every assertion of the other tests gives the same answer under ValueType.Equals, which compares the same fields
    // through reflection; only the declaring type of the methods shows whether the struct overrides them.
    [Theory]
    [InlineData(typeof(GpuBigUIntArray))]
    [InlineData(typeof(GpuBigIntArray))]
    [InlineData(typeof(GpuBigFloatArray))]
    [Scenario(TestScenario.HappyPath)]
    public void EqualsAndGetHashCode_EachArrayType_AreDeclaredOnTheStructNotValueType(Type arrayType)
    {
        // mutant: delete the Equals(object?) override of the struct (ValueType.Equals takes over)
        Assert.Equal(arrayType, arrayType.GetMethod(nameof(Equals), [typeof(object)])?.DeclaringType);
        // mutant: delete the GetHashCode override of the struct (ValueType.GetHashCode takes over)
        Assert.Equal(arrayType, arrayType.GetMethod(nameof(GetHashCode), Type.EmptyTypes)?.DeclaringType);
    }

    private static void AssertEqualAndConsistentHash<T>(T left, T right)
        where T : struct, IEquatable<T>
    {
        Assert.True(left.Equals(right));
        Assert.True(right.Equals(left));
        Assert.True(left.Equals((object)right));
        Assert.True(EqualityComparer<T>.Default.Equals(left, right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.Equal(EqualityComparer<T>.Default.GetHashCode(left), EqualityComparer<T>.Default.GetHashCode(right));
    }

    private static void AssertNotEqual<T>(T left, T right)
        where T : struct, IEquatable<T>
    {
        Assert.False(left.Equals(right));
        Assert.False(right.Equals(left));
        Assert.False(left.Equals((object)right));
        Assert.False(EqualityComparer<T>.Default.Equals(left, right));
    }
}
