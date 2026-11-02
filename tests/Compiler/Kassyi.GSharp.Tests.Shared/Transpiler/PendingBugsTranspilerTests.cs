#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Transpiler and generator tests pinning current behavior for open issues
/// (#543, #641, #1081, #1084, #1085, #1086).
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class PendingBugsTranspilerTests : TranspilerTestBase
{
    // #641: Expect Gpu.Constant("42") to be rewritten for managed execution instead of remaining in the generated C# output.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_GpuConstantMacro_LeavesGpuConstantCallInManagedOutput()
    {
        var source = RoslynTestUtility.ReadTestCase(
            "Transpiler/PendingBugs/GpuConstantManagedKernels.cu.cs");

        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, "Cpu");
        var runResult = driver.GetRunResult();
        var generatedSources = runResult.GeneratedTrees
            .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.GetText().ToString())
            .ToArray();

        Assert.NotEmpty(generatedSources);
        var combinedGenerated = string.Join(Environment.NewLine, generatedSources);

        Assert.Contains("Gpu.Constant(\"42\")", combinedGenerated, StringComparison.Ordinal);
    }

    // #1081: Expect TakeLast, SkipLast, unfused Sum(selector), and unfused LastOrDefault(predicate) to fuse, emit valid StackLinq.cuh helpers, or report a diagnostic.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TakeLastWithElementAtOrDefault_EmitsUndefinedStackLinqCall()
    {
        var (_, tree, model) =
            RoslynTestUtility.GetTestCaseContext("Transpiler/PendingBugs/UnfusedStackLinqMissingTemplatesKernels.cu.cs");

        var takeLastMethod = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "TakeLastElementAtOrDefaultKernel");
        var unfusedSelectorMethod = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "UnfusedSelectorSumAndPredicateLastOrDefaultKernel");

        var failures1 = new List<TranspileFailure>();
        var cppTakeLast = MethodTranspiler.GenerateCudaSource(
            takeLastMethod, model, failures1.Add, isDevice: false, CurrentDialect, out _);

        var failures2 = new List<TranspileFailure>();
        var cppUnfusedSelector = MethodTranspiler.GenerateCudaSource(
            unfusedSelectorMethod, model, failures2.Add, isDevice: false, CurrentDialect, out _);

        Assert.Contains("StackLinq::TakeLast(", cppTakeLast, StringComparison.Ordinal);
        Assert.Contains("StackLinq::SkipLast(", cppTakeLast, StringComparison.Ordinal);
        Assert.Contains("StackLinq::Sum(__gsharp_id_", cppUnfusedSelector, StringComparison.Ordinal);
        Assert.Contains("StackLinq::LastOrDefault(__gsharp_id_", cppUnfusedSelector, StringComparison.Ordinal);
    }

    // #1086: Expect an unfused StackLinq query with a capturing lambda stored in a local to compile on native C++ without C2338 static_assert failure or report a diagnostic.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_UnfusedStackLinqQueryWithCapturingLambdaInLocal_EmitsEnumeratorStructWithMismatchedSizeCheck()
    {
        var (_, tree, model) =
            RoslynTestUtility.GetTestCaseContext("Transpiler/PendingBugs/UnfusedStackLinqCapturingLambdaLocalKernels.cu.cs");

        var methodDecl = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "UnfusedQueryWithCapturingLambdaInLocalKernel");

        var failures = new List<TranspileFailure>();
        var cppSource = MethodTranspiler.GenerateCudaSource(
            methodDecl, model, failures.Add, isDevice: false, new StandardCppDialect(), out _);

        Assert.Empty(failures);
        Assert.Contains(
            "Kassyi.ZeroAlloc.Linq.StackEnumerable<Kassyi.ZeroAlloc.Linq.TakeEnumerator<Kassyi.ZeroAlloc.Linq.SelectEnumerator<",
            cppSource,
            StringComparison.Ordinal);
        Assert.Contains(": size\");", cppSource, StringComparison.Ordinal);
    }

    // #1084: Expect outputCompilation.GetDiagnostics() to have zero errors once ContextHoister avoids hoisting non-crossing SharedMemory<T> locals to outer scope.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_SharedMemorySiblingScopes_ProducesCS0136InGeneratedManagedCSharp()
    {
        var source = RoslynTestUtility.ReadTestCase(
            "Transpiler/PendingBugs/SharedMemorySiblingScopeHoistKernels.cu.cs");

        var (_, outputCompilation, _) = RoslynTestUtility.RunGenerator(source, "Cpu");
        var compilerErrors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Equal(2, compilerErrors.Length);
        Assert.All(compilerErrors, e => Assert.Equal("CS0136", e.Id));
    }

    // #1085: Expect C++ declarations for parenthesized creation `s` and alias copy `alias` to be emitted once LocalDeclarationStatementTranspilerRule handles non-direct SharedMemory<T> initializers.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_ParenthesizedAndAliasSharedMemoryLocal_OmitsCppVariableDeclarations()
    {
        var (_, tree, model) =
            RoslynTestUtility.GetTestCaseContext("Transpiler/PendingBugs/SharedMemoryDeclarationAndTypeMismatchKernels.cu.cs");

        var methodDecl = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "ParenthesizedCreationAndAliasLocalKernel");

        var failures = new List<TranspileFailure>();
        var cppSource = MethodTranspiler.GenerateCudaSource(
            methodDecl, model, failures.Add, isDevice: false, new StandardCppDialect(), out _);

        Assert.Empty(failures);
        Assert.DoesNotContain("int* __gsharp_id_1_s", cppSource, StringComparison.Ordinal);
        Assert.DoesNotContain("int* __gsharp_id_2_alias", cppSource, StringComparison.Ordinal);
    }

    // #1085: Expect C++ declaration and consistent type for `returned = IdentitySharedMemory(localSmem)` once SharedMemory<T> local declarations and CPU representation are unified.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_PassLocalSharedMemoryToHelper_OmitsReturnedLocalDeclarationInCpuDialect()
    {
        var (_, tree, model) =
            RoslynTestUtility.GetTestCaseContext("Transpiler/PendingBugs/SharedMemoryDeclarationAndTypeMismatchKernels.cu.cs");

        var methodDecl = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "PassLocalSharedMemoryToHelperKernel");

        var failures = new List<TranspileFailure>();
        var cppSource = MethodTranspiler.GenerateCudaSource(
            methodDecl, model, failures.Add, isDevice: false, new StandardCppDialect(), out _);

        Assert.Empty(failures);
        Assert.DoesNotContain("__gsharp_id_2_returned =", cppSource, StringComparison.Ordinal);
    }

    // #543: Expect distinct site keys (Assert.NotEqual) for AliasInStruct<int> and AliasInStruct<float> once SharedMemorySiteKey includes constructed struct type arguments.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TypeAliasSharedMemoryInGenericStruct_EmitsCollidingSiteKeysAcrossTypeArguments()
    {
        var (_, tree, model) =
            RoslynTestUtility.GetTestCaseContext("Transpiler/PendingBugs/GenericStructSharedMemorySiteKeyKernels.cu.cs");

        var methodDecl = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "UseAliasInGenericStructKernel");

        var failures = new List<TranspileFailure>();
        var cppSource = MethodTranspiler.GenerateCudaSource(
            methodDecl, model, failures.Add, isDevice: false, new StandardCppDialect(), out _);

        Assert.Empty(failures);

        var siteKeys = Regex.Matches(cppSource, @"__gsharp_allocate_smem_static\((0x[0-9a-fA-F]+ull)")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        Assert.Equal(2, siteKeys.Length);
        Assert.Equal(siteKeys[0], siteKeys[1]);
    }

    // #543: Expect 4 distinct site keys for generic struct constructors/operators and valid site key computation in property getters once SharedMemorySiteKey supports them.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_GenericStructSharedMemoryInCtorOperatorAndProperty_CollidesOnSiteKeysAndThrowsInPropertyGetter()
    {
        var (_, tree, model) =
            RoslynTestUtility.GetTestCaseContext("Transpiler/PendingBugs/GenericStructSharedMemorySiteKeyKernels.cu.cs");

        var ctorAndOpMethod = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "UseGenericCtorAndOpBufKernel");

        var failures1 = new List<TranspileFailure>();
        var cppCtorAndOp = MethodTranspiler.GenerateCudaSource(
            ctorAndOpMethod, model, failures1.Add, isDevice: false, new StandardCppDialect(), out _);

        Assert.Empty(failures1);

        var ctorAndOpKeys = Regex.Matches(cppCtorAndOp, @"__gsharp_allocate_smem_static\((0x[0-9a-fA-F]+ull)")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        Assert.Equal(2, ctorAndOpKeys.Length);
        Assert.Single(ctorAndOpKeys.Distinct(StringComparer.Ordinal));

        var computedPropMethod = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "UseComputedPropSharedMemoryBufKernel");

        var failures2 = new List<TranspileFailure>();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MethodTranspiler.GenerateCudaSource(
                computedPropMethod, model, failures2.Add, isDevice: false, new StandardCppDialect(), out _));
        Assert.Contains("not inside a method", ex.Message, StringComparison.Ordinal);
    }
}
