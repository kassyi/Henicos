
namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct DummyStruct_GEN17
{
    public int Field;
}

public class TestClass_GEN17
{
    public static void ModifyRef(ref bool successFlag)
    {
        successFlag = false;
    }

    public static void MemberAccessRef(ref DummyStruct_GEN17 refArg)
    {
        refArg.Field = 5;
    }

    public static void ChainedRefAssignment(ref int refArg, out int a, out int b)
    {
        a = b = refArg = 10;
    }
}

public partial class RefOutDereferenceTests
    : TranspilerTestBase
{
    [GeneratedRegex(@"\(\*successFlag\)\s*=\s*false;")]
    private static partial Regex SuccessFlagRegex();

    [GeneratedRegex(@"\(\*refArg\)\.Field\s*=\s*5;")]
    private static partial Regex RefArgFieldRegex();

    [GeneratedRegex(@"\(\*a\)\s*=\s*\(\*b\)\s*=\s*\(\*refArg\)\s*=\s*10;")]
    private static partial Regex ChainedRefRegex();

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    public void Test_RefOutDereferencing_HappyPath_ModifyingRefArgument()
    {
        var (cudaSource, diagnostics) =
            RoslynTestUtility.TranspileMethod<TestClass_GEN17>(nameof(TestClass_GEN17.ModifyRef));

        Assert.Empty(diagnostics);
        Assert.Matches(SuccessFlagRegex(), cudaSource.WithCSharpNames());
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    public void Test_RefOutDereferencing_EdgeCase_MemberAccessAndChainedAssignments()
    {
        var (cudaSourceMember, diagnosticsMember) =
            RoslynTestUtility.TranspileMethod<TestClass_GEN17>(nameof(TestClass_GEN17.MemberAccessRef));
        Assert.Empty(diagnosticsMember);
        Assert.Matches(RefArgFieldRegex(), cudaSourceMember.WithCSharpNames());

        var (cudaSourceChained, diagnosticsChained) =
            RoslynTestUtility.TranspileMethod<TestClass_GEN17>(nameof(TestClass_GEN17.ChainedRefAssignment));
        Assert.Empty(diagnosticsChained);
        Assert.Matches(ChainedRefRegex(), cudaSourceChained.WithCSharpNames());
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RefReadOnlyAndInParameters_ReadForwardAndMemberAccess()
    {
        const int count = 4;
        int[] input = [1, 2, 5, 10];
        var output = new int[count * 4];

        RefOutDereferenceKernels.LaunchRefReadOnlyAndInParameterKernel(input, output, blockDimX: count);

        for (int i = 0; i < count; i++)
        {
            int x = input[i];
            int y = x + 3;
            int expectedDirectInt = (x + 10) * 1000 + (y + 20);
            int expectedForwardInt = ((x + 10) + (x + 20) + (x + 20)) * 1000 +
                                     ((y + 20) + (y + 10) + (y + 20));
            int s1ReadRefReadOnly = x * 100 + 7;
            int s1ReadIn = x * 10 + 7;
            int s2ReadRefReadOnly = y * 100 + 9;
            int s2ReadIn = y * 10 + 9;
            int expectedDirectStruct = s1ReadRefReadOnly * 1000 + s2ReadIn;
            int expectedForwardStruct = s1ReadRefReadOnly + s2ReadIn + s1ReadIn + s2ReadRefReadOnly;

            Assert.Equal(expectedDirectInt, output[i * 4]);
            Assert.Equal(expectedForwardInt, output[i * 4 + 1]);
            Assert.Equal(expectedDirectStruct, output[i * 4 + 2]);
            Assert.Equal(expectedForwardStruct, output[i * 4 + 3]);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_RefReadOnlyAndInParameters_RValueTemporaryArguments()
    {
        const int count = 4;
        int[] input = [1, 2, 5, 10];
        var output = new int[count * 6];

        RefOutDereferenceKernels.LaunchRValueArgumentsForReadOnlyRefKernel(input, output, blockDimX: count);

        for (int i = 0; i < count; i++)
        {
            int x = input[i];
            int computedSum = x + 4;
            int expectedConvertedAndRefThis =
                (x + 30) * 1000000 +
                ((x + 1) + 30) * 10000 +
                (2 * x + 1) * 100 +
                5 * 10 +
                4;
            Assert.Equal((5 + 20) * 1000 + ((x + 1) + 20), output[i * 6]);
            Assert.Equal(x * 10 + 4, output[i * 6 + 1]);
            Assert.Equal(((x + 2) + 20) + ((x + 2) + 10) + ((x + 2) + 20), output[i * 6 + 2]);
            Assert.Equal((i + 20) * 10000 + (count + 20) * 100 + (count + 20), output[i * 6 + 3]);
            Assert.Equal((computedSum + 20) * 10000 + (computedSum + 20) * 100 + (15 + 20), output[i * 6 + 4]);
            Assert.Equal(expectedConvertedAndRefThis, output[i * 6 + 5]);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_KernelInParameters_ReadAndForwardToDeviceFunctions()
    {
        const int count = 4;
        var output = new int[count * 3];
        var s = new RefTestStruct { Field = 3, Value = 8 };

        RefOutDereferenceKernels.LaunchKernelWithInParameters(42, s, output, blockDimX: count);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(42 + 3 * 100 + 8, output[i * 3]);
            Assert.Equal((42 + 20) * 1000 + (42 + 10), output[i * 3 + 1]);
            Assert.Equal((3 * 10 + 8) * 1000 + (3 * 100 + 8), output[i * 3 + 2]);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_AutoPropertyPassedToInParameter_CopiesValueInsteadOfAliasingBackingField()
    {
        const int count = 4;
        int[] input = [7, 12, -3, 0];
        var output = new int[count * 4];

        RefOutDereferenceKernels.LaunchAutoPropertyInParameterSnapshotKernel(input, output, blockDimX: count);

        for (int i = 0; i < count; i++)
        {
            int x = input[i];
            Assert.Equal(x, output[i * 4]);
            Assert.Equal(99, output[i * 4 + 1]);
            Assert.Equal(x * 10 + 4, output[i * 4 + 2]);
            Assert.Equal(88, output[i * 4 + 3]);
        }

#if GSHARP_CPU
        var managedOutput = new int[count * 4];
        ManagedKernelRun.Run(() =>
            RefOutDereferenceKernels.LaunchAutoPropertyInParameterSnapshotKernel(input, managedOutput, blockDimX: count));

        for (int i = 0; i < count; i++)
        {
            int x = input[i];
            Assert.Equal(x, managedOutput[i * 4]);
            Assert.Equal(99, managedOutput[i * 4 + 1]);
            Assert.Equal(x * 10 + 4, managedOutput[i * 4 + 2]);
            Assert.Equal(88, managedOutput[i * 4 + 3]);
        }
#endif
    }
}
