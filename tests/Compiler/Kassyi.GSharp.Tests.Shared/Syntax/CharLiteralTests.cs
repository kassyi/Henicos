using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A character literal keeps its UTF-16 value in the kernel, written directly or as an escape.</summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class CharLiteralTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_CharLiteral_KeepsCharacterCode_Execution()
    {
        int[] output = new int[10];
        char[] text = new char[2];

        CharLiteralKernels.LaunchCharLiteralKernel(output, text, blockDimX: 1);

        Assert.Equal([0x41, 0x0A, 0xE9, 0xFFFF, 0xE9, 0xFFFF, 0x27, 0x5C, 0xD800, 0], output);
        Assert.Equal([(char)0xE9, (char)0xFFFF], text);
    }

    // The native CPU delegate must pass char parameters as 16-bit UTF-16 code units, not through ANSI conversion (#971)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, '\0', 'A')]
    [InlineData(false, '\u007F', '\u0080')]
    [InlineData(false, '\u00E9', '\u266F')]
    [InlineData(false, '\u3042', '\uD800')]
    [InlineData(false, '\uFFFF', '\u1234')]
    [InlineData(true, '\0', 'A')]
    [InlineData(true, '\u007F', '\u0080')]
    [InlineData(true, '\u00E9', '\u266F')]
    [InlineData(true, '\u3042', '\uD800')]
    [InlineData(true, '\uFFFF', '\u1234')]
    public void Test_CharArgument_KeepsUtf16CodeUnit_Execution(bool forceManaged, char value, char inValue)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        int[] codes = new int[3];
        char[] text = new char[2];
        char refValue = '\u266F';

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        char outValue;
        try
        {
            CharLiteralKernels.LaunchCharArgumentKernel(
                codes,
                text,
                value,
                inValue,
                ref refValue,
                out outValue,
                blockDimX: 1);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }

        Assert.Equal([(int)value, (int)inValue, 0x266F], codes);
        Assert.Equal([value, inValue], text);
        Assert.Equal(value, refValue);
        Assert.Equal(inValue, outValue);
    }
}
