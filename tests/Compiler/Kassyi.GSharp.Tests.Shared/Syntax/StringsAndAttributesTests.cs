using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct Pair_StringsTest<T> where T : unmanaged
{
    public T First;
    public T Second;
}

public class StringsAndAttributesTarget
{
    public static void StringInterpolationPrintf(uint[] data)
    {
        uint reg = data[0];
        Gpu.InlinePtx($"mov.u32 {reg}, 42;");
        data[0] = reg;
    }

    public static void Utf8StringLiteral(byte[] output)
    {
        ReadOnlySpan<byte> utf8 = "GPU"u8;
        output[0] = utf8[0];
    }

    public static void NameOfExpression(int[] output)
    {
        string name = nameof(output);
        output[0] = name.Length;
    }

    public static void LoopUnrollAttribute(int[] output)
    {
        int acc = 0;
        Gpu.Hint.Unroll(4);
        for (int i = 0; i < 4; i++)
        {
            acc += i;
        }
        output[0] = acc;
    }

    public static void GenericStruct(Pair_StringsTest<int>[] items, int[] output)
    {
        Pair_StringsTest<int> p = items[0];
        output[0] = p.First + p.Second;
    }
}

public class StringsAndAttributesTests : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Syntax)]
    public async Task Test_StringInterpolationPrintf_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<StringsAndAttributesTarget>(
            nameof(StringsAndAttributesTarget.StringInterpolationPrintf), isDevice: false, dialect: CurrentDialect);

        // Dialects without InlineAssembly (CPU) must replace the call with a runtime failure and warn.
        if (CurrentDialect.Capabilities.HasFlag(DialectCapabilities.InlineAssembly))
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            diagnostics.AssertContains(GSharpDiagnostics.InlinePtxNotSupportedByDialect);
            Assert.Contains("__gsharp_unsupported(", cudaSource);
        }

        await Verify(Report(cudaSource, diagnostics));
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public async Task Test_Utf8StringLiteral_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<StringsAndAttributesTarget>(
            nameof(StringsAndAttributesTarget.Utf8StringLiteral), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(Report(cudaSource, diagnostics));

        byte[] output = new byte[1];
        StringsAndAttributesTarget.Utf8StringLiteral(output);
        Assert.Equal((byte)'G', output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public async Task Test_NameOfExpression_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<StringsAndAttributesTarget>(
            nameof(StringsAndAttributesTarget.NameOfExpression), isDevice: false, dialect: CurrentDialect);

        // The analyzer reports the string variable (UnsupportedConstructDiagnosticsTests); the transpiler reports nothing
        Assert.Empty(diagnostics);
        await Verify(Report(cudaSource, diagnostics));
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public async Task Test_LoopUnrollAttribute_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<StringsAndAttributesTarget>(
            nameof(StringsAndAttributesTarget.LoopUnrollAttribute), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(Report(cudaSource, diagnostics));

        int[] output = new int[1];
        StringsAndAttributesTarget.LoopUnrollAttribute(output);
        Assert.Equal(6, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public async Task Test_GenericStruct_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<StringsAndAttributesTarget>(
            nameof(StringsAndAttributesTarget.GenericStruct), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(Report(cudaSource, diagnostics));

        Pair_StringsTest<int>[] items = [new() { First = 10, Second = 25 }];
        int[] output = new int[1];
        StringsAndAttributesTarget.GenericStruct(items, output);
        Assert.Equal(35, output[0]);
    }
}
