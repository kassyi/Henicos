#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// In-memory generator and transpiler verification for language-feature cases that currently produce
/// invalid C++ or unexpected diagnostics, kept out of <c>.cu.cs</c> native build targets so
/// <c>Tests.Cpu</c> and <c>Tests.Cuda</c> compile cleanly. Each test asserts the correct expected result
/// and remains skipped until the corresponding compiler issue is fixed.
/// </summary>
[Category(TestCategory.Compiler)]
[Scenario(TestScenario.SadPath)]
public class LanguageFeaturesPendingBugsTests
{
    private static string GetGeneratedDeviceCode(GeneratorDriver driver, string? treeHintSubstring = null)
    {
        var runResult = driver.GetRunResult();
        var trees = treeHintSubstring is null
            ? runResult.GeneratedTrees
            : runResult.GeneratedTrees.Where(t => t.FilePath.Contains(treeHintSubstring, StringComparison.Ordinal));
        return string.Join(
            "\n",
            trees.Select(static t => t.GetText().ToString()));
    }

    [Fact(Skip = "Pending #1062: Generic [GpuDeviceFunction] lacks template header and call-site type arguments")]
    public void Test_GenericDeviceFunction_EmitsTemplateHeaderAndCallTypeArgs()
    {
        string source = RoslynTestUtility.ReadTestCase("Transpiler/LanguageFeaturesPending/GenericDeviceFunctionCase.cs");
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, "Cpu");
        string code = GetGeneratedDeviceCode(driver);

        Assert.DoesNotContain("unsubstituted type parameter", code, StringComparison.Ordinal);
        Assert.Contains("template <typename", code, StringComparison.Ordinal);
        Assert.DoesNotContain("__gsharp_fn_GenericDeviceFunctionCase__Pick_a1(", code, StringComparison.Ordinal);
    }

    [Fact(Skip = "Pending #464: Indexed StackLinq overloads (TakeWhile/SkipWhile/Select/Where) emit 1-arg calls in fused C++ and non-At helpers in unfused C++")]
    public void Test_StackLinqIndexedOverloads_EmitIndexedCalls()
    {
        string source = RoslynTestUtility.ReadTestCase("Transpiler/LanguageFeaturesPending/IndexedStackLinqCase.cs");
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "IndexedStackLinqCase");

        Assert.DoesNotContain(")(__gsharp_val_0))", code, StringComparison.Ordinal);
        Assert.Contains("StackLinq::TakeWhileAt(", code, StringComparison.Ordinal);
        Assert.Contains("StackLinq::SkipWhileAt(", code, StringComparison.Ordinal);
    }

    [Fact(Skip = "Pending #1144: StackLinq Aggregate with named arguments (func:, seed:) misbinds seed and accumulator lambda")]
    public void Test_StackLinqAggregateNamedArguments_BindsSeedAndAccumulatorCorrectly()
    {
        string source = RoslynTestUtility.ReadTestCase("Transpiler/LanguageFeaturesPending/AggregateBindingCase.cs");
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "AggregateBindingCase");

        Assert.DoesNotContain("(10)(", code, StringComparison.Ordinal);
        Assert.Contains("10", code, StringComparison.Ordinal);
    }

    [Fact(Skip = "Pending #1081: StackLinq TakeLast followed by unfused terminal emits undefined helper in C++")]
    public void Test_StackLinqTakeLastWithUnfusedTerminal_EmitsValidCpp()
    {
        string source = RoslynTestUtility.ReadTestCase("Transpiler/LanguageFeaturesPending/TakeLastUnfusedCase.cs");
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "TakeLastUnfusedCase");

        Assert.DoesNotContain("StackLinq::TakeLast(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Test_UserDefinedInlinePtxMethod_IsNotRewrittenToThrow()
    {
        string source = RoslynTestUtility.ReadTestCase("Transpiler/LanguageFeaturesPending/UserInlinePtxCase.cs");
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "UserInlinePtxCase");

        Assert.DoesNotContain("Gpu.InlinePtx is not supported", code, StringComparison.Ordinal);
        Assert.Contains("InlinePtx", code, StringComparison.Ordinal);
    }

    [Fact(Skip = "Pending #1073: Operator macro inside struct Dispose leaves managed struct Dispose unrewritten")]
    public void Test_OperatorMacroInsideStructDispose_RewritesManagedStructDispose()
    {
        string source = RoslynTestUtility.ReadTestCase("Transpiler/LanguageFeaturesPending/OperatorMacroInsideStructDisposeCase.cs");
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "MacroKernelHolder");

        Assert.Contains("__gsharp_managed_Dispose", code, StringComparison.Ordinal);
    }

    [Fact(Skip = "Pending #610: Mutating struct property getter emitted as const member function in C++")]
    public void Test_MutatingStructPropertyGetter_OmitsConstQualifier()
    {
        string source = RoslynTestUtility.ReadTestCase("Transpiler/LanguageFeaturesPending/MutatingStructPropertyGetterCase.cs");
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, "Cpu");
        string code = GetGeneratedDeviceCode(driver);

        Assert.DoesNotContain("__device__ inline int __gsharp_mb_StepState() const", code, StringComparison.Ordinal);
        Assert.Contains("__device__ inline int __gsharp_mb_StepState()", code, StringComparison.Ordinal);
    }

    [Fact(Skip = "Pending #1145: Nested struct inside generic outer class leaves outer type parameter unsubstituted")]
    public void Test_NestedStructInsideGenericOuterType_SubstitutesOuterTypeParameters()
    {
        string source = RoslynTestUtility.ReadTestCase("Transpiler/LanguageFeaturesPending/NestedStructInsideGenericOuterCase.cs");
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "GenericOuterKernelHolder");

        Assert.DoesNotContain("GSH0003:", code, StringComparison.Ordinal);
        Assert.DoesNotContain("unsubstituted type parameter", code, StringComparison.Ordinal);
    }

    [Fact(Skip = "Pending #1075: MemoryExtensions.Sort with generic method group reports GSH0003")]
    public void Test_SpanSortWithGenericMethodGroup_CompilesWithoutGSH0003()
    {
        string source = RoslynTestUtility.ReadTestCase("Transpiler/LanguageFeaturesPending/GenericSortMethodGroupCase.cs");
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "GenericSortComparisonCase");

        Assert.DoesNotContain("GSH0003:", code, StringComparison.Ordinal);
    }

    [Fact(Skip = "Pending #674, #963, #1033: User-defined explicit operator and custom struct indexer omitted from emitted C++ struct")]
    public void Test_ExplicitConversionAndCustomIndexer_EmittedInCppStruct()
    {
        string source = RoslynTestUtility.ReadTestCase("Transpiler/LanguageFeaturesPending/ExplicitConversionAndCustomIndexerCase.cs");
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "ExplicitAndIndexedKernelHolder");

        Assert.DoesNotContain("(__gsharp_ty_ExplicitAndIndexedStruct)__gsharp_id_2_x", code, StringComparison.Ordinal);
        Assert.Contains("operator[](", code, StringComparison.Ordinal);
    }
}
