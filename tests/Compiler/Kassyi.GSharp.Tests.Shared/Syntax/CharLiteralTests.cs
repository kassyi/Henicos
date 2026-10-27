namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>A character literal keeps its UTF-16 value in the kernel, written directly or as an escape.</summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
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
}
