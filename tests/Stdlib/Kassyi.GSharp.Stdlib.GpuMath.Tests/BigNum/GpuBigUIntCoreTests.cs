
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuUIntBigIntegerCoreTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuUIntBigIntegerCoreTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("0", 1)]
    [InlineData("123456789012345678901234567890", 4)]
    [InlineData("-123456789012345678901234567890", 4)] // Should take absolute value
    public void Conversion_FromAndToBigInteger_RoundTripsCorrectly(string valueStr, int minWords)
    {
        CudaCapability.EnsureGpuAvailable();
        var original = BigInteger.Parse(valueStr);
        using var gpuBigInt = GpuBigUInt.FromBigInteger(_context, original, minWords);
        
        Assert.True(gpuBigInt.NumWords >= minWords);

        var roundTripped = gpuBigInt.ToBigInteger();
        Assert.Equal(BigInteger.Abs(original), roundTripped);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void PadTo_LargerSize_ZeroPadsCorrectly()
    {
        CudaCapability.EnsureGpuAvailable();
        var original = new BigInteger(0xDEADBEEF);
        using var gpuBigInt = GpuBigUInt.FromBigInteger(_context, original, 1);
        
        using var padded = gpuBigInt.PadTo(4);
        Assert.Equal(4, padded.NumWords);
        
        var hostData = padded.ToHostArray();
        Assert.Equal(0xDEADBEEF, hostData[0]);
        Assert.Equal(0u, hostData[1]);
        Assert.Equal(0u, hostData[2]);
        Assert.Equal(0u, hostData[3]);
        
        Assert.Equal(original, padded.ToBigInteger());
    }

    [SkippableFact]
    public void PadToAndTruncate_SmallerSize_ThrowsOutOfRangeAndNarrows()
    {
        CudaCapability.EnsureGpuAvailable();
        var originalData = new uint[] { 1, 2, 3, 4, 5 };
        using var original = new GpuBigUInt(_context, originalData);

        // PadTo only widens; narrowing is explicit (Truncate: unchecked, Fit: checked)
        Assert.Throws<ArgumentOutOfRangeException>(() => original.PadTo(3));

        using var truncated = original.Value.Truncate(3).Node.Evaluate(_context);
        Assert.Equal(3, truncated.NumWords);
        Assert.Equal(new uint[] { 1, 2, 3 }, truncated.ToHostArray());
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Add_TwoOperands_EvaluatesCorrectly()
    {
        CudaCapability.EnsureGpuAvailable();
        var originalA = BigInteger.Parse("1234567890123456789012345678901234567890");
        var originalB = BigInteger.Parse("9876543210987654321098765432109876543210");
        
        using var a = GpuBigUInt.FromBigInteger(_context, originalA);
        using var b = GpuBigUInt.FromBigInteger(_context, originalB);

        var expr = a + b;
        using var result = expr.Node.Evaluate(_context, null);

        Assert.Equal(originalA + originalB, result.ToBigInteger());
    }

    // ---- Argument checks (#598 stage 2b) ----

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void Create_RejectsNonZeroExponentAndNegativeNonZero()
    {
        using var nonZero = new GpuBigUInt(_context, [7u, 0u]);
        using var zero = new GpuBigUInt(_context, 2);

        // An integer has exponent 0.
        // mutant: GpuBigUInt.cs:53 `exponent != 0` -> `exponent == 0`
        var exponentError = Assert.Throws<ArgumentOutOfRangeException>(() => GpuBigUInt.Create(nonZero, 1, false));
        Assert.Equal("exponent", exponentError.ParamName);

        // An unsigned value cannot be negative, except zero (negative zero is zero).
        // mutant: GpuBigUInt.cs:58 `&&` -> `||` (zero, isNegative: true) / `!magnitude.IsZero()` -> `magnitude.IsZero()`
        var signError = Assert.Throws<ArgumentOutOfRangeException>(() => GpuBigUInt.Create(nonZero, 0, true));
        Assert.Equal("isNegative", signError.ParamName);
        Assert.Equal(zero.DevicePointer, GpuBigUInt.Create(zero, 0, true).DevicePointer);
        Assert.Equal(nonZero.DevicePointer, GpuBigUInt.Create(nonZero, 0, false).DevicePointer);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void Constructor_RejectsInvalidArguments()
    {
        // A value has at least one word.
        // mutant: GpuBigUInt.cs:124 `numWords <= 0` -> `numWords < 0` (0 words)
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuBigUInt(_context, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuBigUInt(_context, -1));

        // Host data must be given and must not be empty.
        // mutant: GpuBigUInt.cs:141 `ArgumentNullException.ThrowIfNull(hostData)` removed (NullReferenceException instead)
        // mutant: GpuBigUInt.cs:142 `hostData.Length == 0` -> `!=` / throw removed
        Assert.Throws<ArgumentNullException>(() => new GpuBigUInt(_context, (uint[])null));
        Assert.Throws<ArgumentException>(() => new GpuBigUInt(_context, Array.Empty<uint>()));

        // A value allocated by the constructor owns its buffer.
        using var owned = new GpuBigUInt(_context, 4);
        Assert.True(owned.OwnsBuffer);
        Assert.Equal(4, owned.NumWords);

        // An unsigned value is never negative.
        // mutant: GpuBigUInt.cs:42 `CanBeNegative => false` -> `true`
        Assert.False(owned.IsNegative);
        Assert.False(owned.CanBeNegative);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void SetIsNegative_SetsAndClearsOnlyTheTopBit()
    {
        // SetIsNegative(true) sets bit 31 of the top word and keeps it set when it already is; false clears it.
        // The other bits of the top word and the lower words are unchanged.
        // mutant: GpuBigUInt.cs:528 `word |= 0x80000000` -> `word ^= 0x80000000` (second call)
        using var value = new GpuBigUInt(_context, [5u, 0x1234_5678u]);
        value.SetIsNegative(true);
        value.SetIsNegative(true);
        Assert.True(value.GetIsNegative());
        Assert.Equal(new uint[] { 5u, 0x9234_5678u }, value.ToHostArray());

        value.SetIsNegative(false);
        Assert.False(value.GetIsNegative());
        Assert.Equal(new uint[] { 5u, 0x1234_5678u }, value.ToHostArray());
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Square_RequiresTwiceTheLogicalWords()
    {
        // The square of an n-word value needs 2n words; one word less is rejected.
        // mutant: GpuBigUInt.cs:546 `NumWords < logicalWords * 2` -> `NumWords <= logicalWords * 2`
        using var tooSmall = new GpuBigUInt(_context, 3);
        Assert.Throws<InvalidOperationException>(() => tooSmall.Square(2));

        // Exactly 2n words is enough: the low n words are squared in place.
        uint[] words = [uint.MaxValue, 3u, 9u, 9u];
        var logical = new BigInteger(uint.MaxValue) + (new BigInteger(3) << 32);
        using var exact = new GpuBigUInt(_context, words);
        Assert.True(exact.Square(2));
        Assert.Equal(logical * logical, exact.ToBigInteger());
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void Workspace_TempGuards()
    {
        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);

        // A rented value is owned by the caller and returned with Dispose.
        // mutant: GpuWorkspace.RentTemp wraps a borrowed handle
        using (var temp = workspace.RentTemp(4))
        {
            Assert.True(temp.OwnsBuffer);
        }

        workspace.Dispose();

        // After Dispose, RentTemp throws for the workspace itself (not only through the memory manager).
        // mutant: GpuWorkspace.cs:492 `ObjectDisposedException.ThrowIf(...)` removed (the manager throws for itself)
        var rentError = Assert.Throws<ObjectDisposedException>(() => workspace.RentTemp(4));
        Assert.Equal(typeof(GpuWorkspace).FullName, rentError.ObjectName);

        // mutant: DefaultGpuMemoryManager.cs:118 `ObjectDisposedException.ThrowIf(...)` removed
        Assert.Throws<ObjectDisposedException>(() => workspace.MemoryManager.AllocateTemp(4));
    }

    // ---- Device copies and positions (#598 stage 2b) ----
    // The elements 0..2 of a 3 x 4-word array are adjacent, so a write past an element shows up in its neighbours.
    // The element views are not disposed (#630).

    private static readonly uint[] s_arrayData = [1u, 2u, 3u, 4u, 5u, 6u, 7u, 8u, 9u, 10u, 11u, 12u];

    private GpuBigUIntArray CreateFilledArray()
    {
        var array = new GpuBigUIntArray(_context, 3, 4);
        _context.Provider.CopyHostToDevice(array.DevicePointer, s_arrayData, s_arrayData.Length, _context.Stream);
        return array;
    }

    private uint[] ReadArray(GpuBigUIntArray array)
    {
        var host = new uint[array.TotalWords];
        _context.Provider.CopyDeviceToHost(host, array.DevicePointer, host.Length, _context.Stream);
        return host;
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Clear_RangeAndPastEnd()
    {
        using var array = CreateFilledArray();
        var element = array.GetElement(1);

        // Clear(offset, count) zeroes exactly words offset..offset+count-1 of the element.
        // mutant: GpuBigUInt.cs:253 `offsetWords * 4` -> `offsetWords / 4`
        element.Clear(1, 2);
        Assert.Equal(new uint[] { 1u, 2u, 3u, 4u, 5u, 0u, 0u, 8u, 9u, 10u, 11u, 12u }, ReadArray(array));

        // An offset at the end leaves nothing to clear and does not throw.
        // mutant: GpuBigUInt.Clear `if (wordsToClear == 0) return` removed (a zero-length device copy)
        element.Clear(offsetWords: 4);
        Assert.Equal(new uint[] { 1u, 2u, 3u, 4u, 5u, 0u, 0u, 8u, 9u, 10u, 11u, 12u }, ReadArray(array));

        // Clear() zeroes the whole element and nothing else.
        array.GetElement(2).Clear();
        Assert.Equal(new uint[] { 1u, 2u, 3u, 4u, 5u, 0u, 0u, 8u, 0u, 0u, 0u, 0u }, ReadArray(array));
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void CopyFrom_HonorsOffsetsAndBounds()
    {
        using var source = new GpuBigUInt(_context, [101u, 102u, 103u, 104u]);

        // A destination offset copies only the words that fit (2), and the next element is untouched.
        // mutant: GpuBigUInt.cs:278 `NumWords - destOffsetWords` -> `NumWords + destOffsetWords`
        using (var array = CreateFilledArray())
        {
            array.GetElement(0).CopyFrom(source, destOffsetWords: 2);
            Assert.Equal(new uint[] { 1u, 2u, 101u, 102u, 5u, 6u, 7u, 8u, 9u, 10u, 11u, 12u }, ReadArray(array));
        }

        // A source offset copies only the source words that remain (2).
        // mutant: GpuBigUInt.cs:278 `source.NumWords - srcOffsetWords` -> `source.NumWords + srcOffsetWords`
        using (var array = CreateFilledArray())
        {
            array.GetElement(0).CopyFrom(source, srcOffsetWords: 2);
            Assert.Equal(new uint[] { 103u, 104u, 3u, 4u, 5u, 6u, 7u, 8u, 9u, 10u, 11u, 12u }, ReadArray(array));
        }

        // An explicit count writes exactly that many words at the destination offset.
        // mutant: GpuBigUInt.cs:286 `destOffsetWords * 4` -> `destOffsetWords / 4`
        using (var array = CreateFilledArray())
        {
            array.GetElement(0).CopyFrom(source, destOffsetWords: 1, numWords: 1);
            Assert.Equal(new uint[] { 1u, 101u, 3u, 4u, 5u, 6u, 7u, 8u, 9u, 10u, 11u, 12u }, ReadArray(array));
        }
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void ClearWord_ClearsOnlyThatWord()
    {
        // ClearWord(i) zeroes word i of the element.
        // mutant: GpuBigUInt.cs:398 `wordIndex * 4` -> `wordIndex / 4`
        // mutant: GpuBigUInt.cs:399 copy removed
        using var array = CreateFilledArray();
        array.GetElement(1).ClearWord(1);
        Assert.Equal(new uint[] { 1u, 2u, 3u, 4u, 5u, 0u, 7u, 8u, 9u, 10u, 11u, 12u }, ReadArray(array));
    }

    /// <summary>A write on one element of an array, with an 8-word source for <c>CopyFrom</c>.</summary>
    public delegate void ElementWrite(GpuBigUIntView element, GpuBigUInt source);

    /// <summary>
    /// Calls on element 1 of the 3 x 4-word array whose range does not lie within the element, and the parameter
    /// each one names.
    /// </summary>
    public static TheoryData<string, ElementWrite, string> OutOfRangeWrites() => new()
    {
        { "Clear(numWords: 5)", static (e, _) => e.Clear(numWords: 5), "numWords" },
        { "Clear(offsetWords: 2, numWords: 3)", static (e, _) => e.Clear(offsetWords: 2, numWords: 3), "numWords" },
        { "Clear(offsetWords: 5)", static (e, _) => e.Clear(offsetWords: 5), "offsetWords" },
        { "Clear(offsetWords: -1)", static (e, _) => e.Clear(offsetWords: -1), "offsetWords" },
        { "Clear(numWords: -1)", static (e, _) => e.Clear(numWords: -1), "numWords" },
        { "CopyFrom(numWords: 5)", static (e, s) => e.CopyFrom(s, numWords: 5), "numWords" },
        {
            "CopyFrom(destOffsetWords: 2, numWords: 4)",
            static (e, s) => e.CopyFrom(s, destOffsetWords: 2, numWords: 4),
            "numWords"
        },
        { "CopyFrom(destOffsetWords: -1)", static (e, s) => e.CopyFrom(s, destOffsetWords: -1), "destOffsetWords" },
        { "CopyFrom(destOffsetWords: 5)", static (e, s) => e.CopyFrom(s, destOffsetWords: 5), "destOffsetWords" },
        { "CopyFrom(srcOffsetWords: -1)", static (e, s) => e.CopyFrom(s, srcOffsetWords: -1), "srcOffsetWords" },
        { "CopyFrom(srcOffsetWords: 9)", static (e, s) => e.CopyFrom(s, srcOffsetWords: 9), "srcOffsetWords" },
        {
            "CopyFrom(srcOffsetWords: 6, numWords: 3)",
            static (e, s) => e.CopyFrom(s, srcOffsetWords: 6, numWords: 3),
            "numWords"
        },
        { "CopyFrom(numWords: -1)", static (e, s) => e.CopyFrom(s, numWords: -1), "numWords" },
        { "ClearWord(4)", static (e, _) => e.ClearWord(4), "wordIndex" },
        { "ClearWord(5)", static (e, _) => e.ClearWord(5), "wordIndex" },
        { "ClearWord(-1)", static (e, _) => e.ClearWord(-1), "wordIndex" },
    };

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(OutOfRangeWrites))]
    public void Writes_OutsideTheElement_ThrowAndLeaveTheArrayUnchanged(
        string call,
        ElementWrite write,
        string paramName)
    {
        // A range outside the element is rejected before any device write, so the neighbouring elements of the
        // array keep their words. The source has 8 words, so a count that fits the source but not the element is
        // caught by the element's bound.
        // mutant: GpuBigUInt.Clear / CopyFrom / ClearWord, any one range check removed (that row writes and does
        // not throw)
        // mutant: GpuBigUInt.ThrowIfOffsetOutOfRange `ThrowIfGreaterThan` -> `ThrowIfGreaterThanOrEqual`
        // (Clear(offsetWords: 4) in Clear_RangeAndPastEnd)
        using var array = CreateFilledArray();
        using var source = new GpuBigUInt(_context, [101u, 102u, 103u, 104u, 105u, 106u, 107u, 108u]);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => write(array.GetElement(1), source));
        Assert.Equal((call, paramName), (call, error.ParamName));
        Assert.Equal(s_arrayData, ReadArray(array));
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void DevicePointer_DefaultAndOffset()
    {
        // A default handle or value has no device memory.
        // mutant: GpuBigIntMemoryHandle.cs:120 `IsDefault ? IntPtr.Zero : ...` -> `!IsDefault ? ...`
        // mutant: GpuBigUInt.cs:75 `Handle.IsDefault ? ...` -> `!Handle.IsDefault ? ...`
        Assert.Equal(IntPtr.Zero, default(GpuBigIntMemoryHandle).DevicePointer);
        Assert.Equal(IntPtr.Zero, default(GpuBigUInt).DevicePointer);
        Assert.Equal(0, default(GpuBigUInt).NumWords);

        // Element i of a 4-word array starts 16 * i bytes after the array.
        // mutant: GpuBigIntMemoryHandle.cs:136 `WordOffset * 4` -> `WordOffset / 4`
        using var array = CreateFilledArray();
        var element = array.GetElement(1);
        Assert.Equal(array.DevicePointer + 16, element.Handle.DevicePointer);
        Assert.Equal(array.DevicePointer + 16, element.DevicePointer);
    }
}
