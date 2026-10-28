
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

[TestTarget(ExecutionTarget.CpuExecution)]
public class CSharpCpuBigIntProviderTests : IDisposable
{
    private readonly GpuWorkspace _workspace;
    private readonly IGpuExecutionProvider _provider;

    public CSharpCpuBigIntProviderTests()
    {
        GpuKernelLoader.ForceCpuFallback = true;
        _workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        _provider = CSharpCpuBigIntProvider.Instance;
    }

    public void Dispose()
    {
        _workspace.Dispose();
        GpuKernelLoader.ForceCpuFallback = false;
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void MemoryOperations_WorkCorrectly()
    {
        IntPtr ptr = _provider.AllocateMemory(4);
        Assert.NotEqual(IntPtr.Zero, ptr);
        _provider.ClearMemoryAsync(ptr, 4, IntPtr.Zero);
        
        _provider.WriteWord(ptr, 0, 12345);
        _provider.WriteWord(ptr, 3, 67890);
        Assert.Equal(12345u, _provider.ReadWord(ptr, 0));
        Assert.Equal(0u, _provider.ReadWord(ptr, 1));
        Assert.Equal(67890u, _provider.ReadWord(ptr, 3));

        uint[] hostData = new uint[] { 1, 2, 3, 4 };
        _provider.CopyHostToDevice(ptr, hostData, 4);
        Assert.Equal(1u, _provider.ReadWord(ptr, 0));
        Assert.Equal(4u, _provider.ReadWord(ptr, 3));

        uint[] backData = new uint[4];
        _provider.CopyDeviceToHost(backData, ptr, 4);
        Assert.Equal(hostData, backData);

        IntPtr destPtr = _provider.AllocateMemory(4);
        _provider.CopyDeviceToDevice(destPtr, ptr, 4);
        Assert.Equal(4u, _provider.ReadWord(destPtr, 3));

        _provider.FreeMemory(ptr);
        _provider.FreeMemory(destPtr);
    }

    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0xFFFFFFFF)]
    public void BasicMath_AddSub_WorksCorrectly(uint rightVal)
    {
        using var a = new GpuBigUInt(_workspace, new uint[] { 100, 0, 0, 0 });
        using var b = new GpuBigUInt(_workspace, new uint[] { rightVal, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 4);

        _provider.Add(_workspace, a, b, res);
        Assert.Equal(100 + rightVal, res.ToHostArray()[0]);

        _provider.Add(_workspace, a, rightVal, res);
        Assert.Equal(100 + rightVal, res.ToHostArray()[0]);

        using var a2 = new GpuBigUInt(_workspace, new uint[] { 100 + rightVal, 0, 0, 0 });
        _provider.Sub(_workspace, a2, b, res);
        Assert.Equal(100u, res.ToHostArray()[0]);

        _provider.Sub(_workspace, a2, rightVal, res);
        Assert.Equal(100u, res.ToHostArray()[0]);
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void BitwiseOperations_WorkCorrectly()
    {
        using var a = new GpuBigUInt(_workspace, new uint[] { 0b1100, 0, 0, 0 });
        using var b = new GpuBigUInt(_workspace, new uint[] { 0b1010, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 4);

        _provider.BitwiseAnd(_workspace, a, b, res);
        Assert.Equal(0b1000u, res.ToHostArray()[0]);

        _provider.BitwiseOr(_workspace, a, b, res);
        Assert.Equal(0b1110u, res.ToHostArray()[0]);

        _provider.BitwiseXor(_workspace, a, b, res);
        Assert.Equal(0b0110u, res.ToHostArray()[0]);

        _provider.BitwiseNot(_workspace, a, res);
        Assert.Equal(~0b1100u, res.ToHostArray()[0]);

        _provider.ShiftLeft(_workspace, a, 2, res);
        Assert.Equal(0b110000u, res.ToHostArray()[0]);

        _provider.ShiftRight(_workspace, a, 1, res);
        Assert.Equal(0b0110u, res.ToHostArray()[0]);
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void MathOperations_WorkCorrectly()
    {
        using var a = new GpuBigUInt(_workspace, new uint[] { 15, 0, 0, 0 });
        using var b = new GpuBigUInt(_workspace, new uint[] { 4, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 4);

        _provider.Mul(_workspace, a, b, res);
        Assert.Equal(60u, res.ToHostArray()[0]);

        _provider.Mul(_workspace, a, 4u, res);
        Assert.Equal(60u, res.ToHostArray()[0]);

        _provider.Square(_workspace, a.DevicePointer, 2);
        Assert.Equal(225u, a.ToHostArray()[0]);

        using var a2 = new GpuBigUInt(_workspace, new uint[] { 12, 0, 0, 0 });
        using var b2 = new GpuBigUInt(_workspace, new uint[] { 18, 0, 0, 0 });
        _provider.Gcd(_workspace, a2, b2, res);
        Assert.Equal(6u, res.ToHostArray()[0]);
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void ModInverse_ThrowsException_WhenNotCoprime()
    {
        using var a = new GpuBigUInt(_workspace, new uint[] { 15, 0, 0, 0 });
        using var n = new GpuBigUInt(_workspace, new uint[] { 20, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 4);

        var exception = Assert.Throws<ArithmeticException>(() => _provider.ModInverse(_workspace, a, n, res));
        Assert.Contains("ModInverse failed", exception.Message);
    }
    
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void ModInverse_WorksCorrectly()
    {
        using var a = new GpuBigUInt(_workspace, new uint[] { 3, 0, 0, 0 });
        using var n = new GpuBigUInt(_workspace, new uint[] { 11, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 4);

        _provider.ModInverse(_workspace, a, n, res);
        Assert.Equal(4u, res.ToHostArray()[0]);
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void GraphOperations_DoNothing()
    {
        IntPtr stream = new IntPtr(123);
        _provider.BeginCapture(stream);
        var graph = _provider.EndCapture(stream);
        Assert.Equal(IntPtr.Zero, graph);

        var exec = _provider.InstantiateGraph(graph);
        Assert.Equal(IntPtr.Zero, exec);

        _provider.LaunchGraph(exec, stream);
        _provider.DestroyGraph(graph, exec);
    }
    
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Checks_WorkCorrectly()
    {
        using var zero = new GpuBigUInt(_workspace, new uint[] { 0, 0, 0, 0 });
        using var one = new GpuBigUInt(_workspace, new uint[] { 1, 0, 0, 0 });
        using var big = new GpuBigUInt(_workspace, new uint[] { 255, 0, 0, 0 });

        Assert.True(_provider.IsZero(_workspace, zero));
        Assert.False(_provider.IsZero(_workspace, one));

        Assert.True(_provider.IsOne(_workspace, one));
        Assert.False(_provider.IsOne(_workspace, zero));

        Assert.Equal(0, _provider.GetBitLength(_workspace, zero));
        Assert.Equal(1, _provider.GetBitLength(_workspace, one));
        Assert.Equal(8, _provider.GetBitLength(_workspace, big));

        Assert.Equal(0, _provider.CompareTo(_workspace, zero, zero));
        Assert.Equal(-1, _provider.CompareTo(_workspace, zero, one));
        Assert.Equal(1, _provider.CompareTo(_workspace, big, one));
    }
    
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void InverseSqrtMega_And_ReciprocalMega_WorkCorrectly()
    {
        using var a = new GpuBigUInt(_workspace, new uint[] { 4, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 8);

        _provider.InverseSqrtMega(_workspace, a, res, 0, 2, 0);
        Assert.False(_provider.IsZero(_workspace, res));
        
        _provider.ReciprocalMega(_workspace, a, res, 0, 32, 0, 0, 32);
        Assert.False(_provider.IsZero(_workspace, res));
    }
    
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void InverseSqrtMega_And_ReciprocalMega_ThrowOnZero()
    {
        using var zero = new GpuBigUInt(_workspace, new uint[] { 0, 0, 0, 0 });
        using var res = new GpuBigUInt(_workspace, 8);

        Assert.Throws<DivideByZeroException>(() => _provider.InverseSqrtMega(_workspace, zero, res, 0, 2, 0));
        Assert.Throws<DivideByZeroException>(() => _provider.ReciprocalMega(_workspace, zero, res, 0, 32, 0, 0, 32));
    }

    // The CPU provider has the exact integer square root on the host: it writes floor(sqrt(a)) whatever the estimate,
    // r² <= a < (r + 1)², over an estimate of all ones.
    // mutant: CSharpCpuBigIntProvider.cs:353 `a.Sqrt()` -> `a.Sqrt() + 1` (all three)
    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(new uint[] { 9u, 0u })]
    [InlineData(new uint[] { 0x1234_5678u, 0x9ABC_DEF0u, 0x0FED_CBA9u, 0x8765_4321u })]
    [InlineData(new uint[] { 0xFFFF_FFFFu, 0xFFFF_FFFFu })]
    public void SqrtFloorCorrection_WritesFloorOfSquareRoot(uint[] aWords)
    {
        var a = ToBigInteger(aWords);
        var rootWords = aWords.Length / 2;
        using var operand = new GpuBigUInt(_workspace, aWords);
        using var square = new GpuBigUInt(_workspace, aWords.Length);
        using var root = new GpuBigUInt(_workspace, Enumerable.Repeat(Sentinel, rootWords).ToArray());
        using var slack = new GpuBigUInt(_workspace, aWords.Length + 1);
        using var trial = new GpuBigUInt(_workspace, aWords.Length + 1);

        _provider.SqrtFloorCorrection(_workspace, operand, square, root, slack, trial);

        var r = ToBigInteger(root.ToHostArray());
        Assert.True(r * r <= a && a < (r + 1) * (r + 1), $"{r} is not floor(sqrt({a}))");
    }

    private const uint Sentinel = 0xFFFFFFFFu;

    private static BigInteger ToBigInteger(uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    private GpuBigUInt CreateSentinelBuffer(int numWords)
    {
        var words = new uint[numWords];
        Array.Fill(words, Sentinel);
        return new GpuBigUInt(_workspace, words);
    }

    // Limbs are read as an unsigned number: a set top bit is a large value, not a negative one.
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Read_TopBitSet_IsUnsigned()
    {
        using var a = new GpuBigUInt(_workspace, [0u, 0u, 0u, 0x80000000u]);
        using var one = new GpuBigUInt(_workspace, [1u, 0u, 0u, 0u]);

        // mutant: CSharpCpuBigIntProvider.cs:41 `isUnsigned: true` -> `false`
        Assert.Equal(1, _provider.CompareTo(_workspace, a, one));
        Assert.Equal(128, _provider.GetBitLength(_workspace, a));
    }

    // ClearMemoryAsync(p, k) zeroes exactly the first k words; k = 0 changes nothing.
    // (A null pointer is not passed: the `||` mutant of L88 would then write through null and crash the test host.)
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void ClearMemoryAsync_ZeroesOnlyRequestedWords()
    {
        var ptr = _provider.AllocateMemory(8);
        try
        {
            var dirty = new uint[8];
            Array.Fill(dirty, Sentinel);
            _provider.CopyHostToDevice(ptr, dirty, 8);

            // mutant: CSharpCpuBigIntProvider.cs:88 `numWords > 0` -> `numWords < 0`, `!= IntPtr.Zero` -> `== IntPtr.Zero`;
            // L90 InitBlock removed, `numWords * 4` -> `numWords / 4`
            _provider.ClearMemoryAsync(ptr, 4, IntPtr.Zero);
            _provider.ClearMemoryAsync(ptr, 0, IntPtr.Zero);

            var back = new uint[8];
            _provider.CopyDeviceToHost(back, ptr, 8);
            Assert.Equal(new[] { 0u, 0u, 0u, 0u, Sentinel, Sentinel, Sentinel, Sentinel }, back);
        }
        finally
        {
            _provider.FreeMemory(ptr);
        }
    }

    // CopyDeviceToHost(host, p, k) writes min(host.Length, k) words and leaves the rest of host alone.
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void CopyDeviceToHost_CopiesOnlyNumWords()
    {
        var ptr = _provider.AllocateMemory(8);
        try
        {
            _provider.CopyHostToDevice(ptr, [1u, 2u, 3u, 4u, 5u, 6u, 7u, 8u], 8);
            var host = new uint[8];
            Array.Fill(host, 0xDEADBEEFu);

            // mutant: CSharpCpuBigIntProvider.cs:120 `Math.Min` -> `Math.Max`
            _provider.CopyDeviceToHost(host, ptr, 4);

            Assert.Equal(new[] { 1u, 2u, 3u, 4u, 0xDEADBEEFu, 0xDEADBEEFu, 0xDEADBEEFu, 0xDEADBEEFu }, host);
        }
        finally
        {
            _provider.FreeMemory(ptr);
        }
    }

    // A result is the value of the operation mod 2^(32 * NumWords), whatever the result buffer held before.
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void ScalarOpsAndSquare_WriteFullResult()
    {
        var modulus = BigInteger.One << 128;
        uint[] aWords = [0x89ABCDEFu, 0x01234567u, 0u, 0u];
        var aValue = ToBigInteger(aWords);
        using var a = new GpuBigUInt(_workspace, aWords);

        // mutant: CSharpCpuBigIntProvider.cs:200 / 208 / 320 `Write(...)` removed (the sentinel would remain)
        using (var res = CreateSentinelBuffer(4))
        {
            _provider.Add(_workspace, a, 7u, res);
            Assert.Equal(aValue + 7, ToBigInteger(res.ToHostArray()));
        }

        using (var res = CreateSentinelBuffer(4))
        {
            _provider.Sub(_workspace, a, 7u, res);
            Assert.Equal(aValue - 7, ToBigInteger(res.ToHostArray()));
        }

        using (var res = CreateSentinelBuffer(4))
        {
            _provider.Mul(_workspace, a, 0x10001u, res);
            Assert.Equal(aValue * 0x10001 % modulus, ToBigInteger(res.ToHostArray()));
        }

        // Square of the 2-word value 2^64 - 1 fills all 4 words of the buffer in place.
        // mutant: CSharpCpuBigIntProvider.cs:266 `logicalWords * 2` -> `logicalWords / 2`, L267 `true` -> `false`
        using var square = new GpuBigUInt(_workspace, [Sentinel, Sentinel, 0u, 0u]);
        Assert.True(_provider.Square(_workspace, square.DevicePointer, 2));
        var max64 = (BigInteger.One << 64) - 1;
        Assert.Equal(max64 * max64, ToBigInteger(square.ToHostArray()));
    }

    public static TheoryData<uint[]> MegaOperands => new()
    {
        { [4u, 0u, 0u, 0u] },
        // Seeded 4-word value with the top word set.
        { [0x3C1F2A5Du, 0x9E37A2B1u, 0x5BD1E995u, 0xC2B2AE35u] },
    };

    // The CPU versions return their definitions: floor(sqrt(floor(2^(2b) / x))) with b = (NumWords + marginWords) * 32,
    // and floor(2^targetBits / x).
    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(MegaOperands))]
    public void InverseSqrtMega_And_ReciprocalMega_MatchDefinition(uint[] xWords)
    {
        const int MarginWords = 1;
        const int TargetBits = 64;
        var x = ToBigInteger(xWords);
        using var operand = new GpuBigUInt(_workspace, xWords);

        // mutant: CSharpCpuBigIntProvider.cs:341 `+ marginWords` -> `- marginWords`, L342 `/ x` -> `* x`
        using (var res = CreateSentinelBuffer(8))
        {
            _provider.InverseSqrtMega(_workspace, operand, res, MarginWords, 0, 0);
            var b = (xWords.Length + MarginWords) * 32;
            var expected = BigFloatReference.IntegerSqrt((BigInteger.One << (2 * b)) / x);
            Assert.Equal(expected, ToBigInteger(res.ToHostArray()));
        }

        // mutant: CSharpCpuBigIntProvider.cs:358 `/ x` -> `* x`, L359 `Write(...)` removed
        using (var res = CreateSentinelBuffer(8))
        {
            _provider.ReciprocalMega(_workspace, operand, res, MarginWords, 0, 0, 0, TargetBits);
            Assert.Equal((BigInteger.One << TargetBits) / x, ToBigInteger(res.ToHostArray()));
        }
    }

    // The inverse r satisfies 0 <= r < n and (a * r) mod n == 1. The inputs drive the extended Euclidean
    // coefficient negative on the way, so the sign handling is exercised.
    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    // mutant: CSharpCpuBigIntProvider.cs:386 `v - t * x` -> `v + t * x`, L394 `v < 0` -> `v > 0` / `!(v < 0)`,
    // L396 `(v + n) % n` -> `(v + n) * n` / `(v - n) % n`
    [InlineData(17u, 3120u, 2753u)]
    [InlineData(42u, 2017u, 1969u)]
    [InlineData(7u, 40u, 23u)]
    public void ModInverse_MatchesDefinition(uint a, uint n, uint expected)
    {
        using var value = new GpuBigUInt(_workspace, [a, 0u, 0u, 0u]);
        using var modulus = new GpuBigUInt(_workspace, [n, 0u, 0u, 0u]);
        using var res = CreateSentinelBuffer(4);

        _provider.ModInverse(_workspace, value, modulus, res);

        var r = ToBigInteger(res.ToHostArray());
        Assert.Equal(new BigInteger(expected), r);
        Assert.True(r < n);
        Assert.Equal(BigInteger.One, a * r % n);
    }
}
