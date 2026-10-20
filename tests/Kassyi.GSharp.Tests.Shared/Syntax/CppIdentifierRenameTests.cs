using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Verifies that C#-legal identifiers which collide with C++/CUDA names compile on every dialect and keep their values.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
public class CppIdentifierRenameTests : TranspilerTestBase
{
    private const int ThreadCount = 32;

    private static int[] CreateSequence(int start)
    {
        var values = new int[ThreadCount];
        for (int i = 0; i < ThreadCount; i++)
        {
            values[i] = start + i;
        }

        return values;
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_KeywordAndAlternativeTokenNames_KeepValues()
    {
        var register = CreateSequence(1);
        var template = new int[ThreadCount];

        CppIdentifierRenameKernels.LaunchKeywordLocalsKernel(register, template, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(8 * (i + 1) + 28, template[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_VerbatimBuiltinAndMacroNames_KeepValues()
    {
        var input = CreateSequence(1);
        var output = new int[ThreadCount];

        CppIdentifierRenameKernels.LaunchVerbatimAndReservedNamesKernel(input, output, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(13 * (i + 1) + 78, output[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_LocalsNamedAfterMappedFunctions_DoNotShadowMathCalls()
    {
        var data = CreateSequence(1);

        CppIdentifierRenameKernels.LaunchMappedFunctionNamedLocalsKernel(data, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            int value = i + 1;
            int min = Math.Min(value, 10);
            int max = Math.Max(value, 10);
            int abs = Math.Abs(min - max);
            int fminf = Math.Min(abs, 5);
            int fmaxf = Math.Max(fminf, 1);
            Assert.Equal(min + max + abs + fminf + fmaxf, data[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ReservedNamesInDeclarationForms_KeepValues()
    {
        var data = CreateSequence(1);

        CppIdentifierRenameKernels.LaunchDeclarationFormsKernel(data, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(2 * (i + 1) + 106, data[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ReservedStructMemberNames_RoundTrip()
    {
        var items = new CppKeywordFields[ThreadCount];
        for (int i = 0; i < ThreadCount; i++)
        {
            int value = i + 1;
            items[i] = new CppKeywordFields
            {
                register = value, @int = 2 * value, Length = 3 * value, Count = 4 * value, _Value = 5 * value
            };
        }

        var result = new int[ThreadCount];

        CppIdentifierRenameKernels.LaunchStructMemberNamesKernel(items, result, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            int value = i + 1;
            Assert.Equal(15 * value, result[i]);
            Assert.Equal(15 * value, items[i].inner.template);
            Assert.Equal(value, items[i].register);
            Assert.Equal(3 * value, items[i].Length);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ReservedNamesAcrossBarrier_SurviveContextHoisting()
    {
        var data = CreateSequence(1);

        CppIdentifierRenameKernels.LaunchBarrierHoistedNamesKernel(data, 10, 20, 30, 40, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            int register = i + 1 + 100;
            int neighbor = (i + 1) % ThreadCount + 1 + 100;
            Assert.Equal(register * 1000 + neighbor, data[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ReservedLambdaParameterName_InGridStrideLoop()
    {
        var data = new int[ThreadCount];

        CppIdentifierRenameKernels.LaunchGridStrideLambdaParameterKernel(data, ThreadCount, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(i * 2, data[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ReservedLabelName_IsAGotoTarget()
    {
        const int Rounds = 3;
        var data = CreateSequence(1);

        CppIdentifierRenameKernels.LaunchKeywordLabelKernel(data, Rounds, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(Rounds * (i + 1), data[i]);
        }
    }
}
