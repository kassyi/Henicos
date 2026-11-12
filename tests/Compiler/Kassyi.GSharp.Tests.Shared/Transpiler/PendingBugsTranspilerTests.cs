#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;
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
    [Fact(Skip = "#641: Gpu.Constant is not rewritten for managed execution.")]
    [Scenario(TestScenario.SadPath)]
    public void GpuConstantMacro_ManagedOutput_RewritesConstantMacro()
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

        Assert.DoesNotContain("Gpu.Constant(\"42\")", combinedGenerated, StringComparison.Ordinal);
        Assert.Contains("42", combinedGenerated, StringComparison.Ordinal);
    }

    // #1081: TakeLast, SkipLast, the unfused Sum(selector) and LastOrDefault(predicate) call functions that StackLinq.cuh
    // defines (StackLinqOperatorTests checks the header against the list)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TakeLastWithElementAtOrDefault_UnfusedOperators_EmitsValidCppWithoutUndefinedCalls()
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

        Assert.Empty(failures1);
        Assert.Empty(failures2);
#pragma warning disable RS0030 // Generated C++ has no syntax tree; its StackLinq calls are matched to DeviceOperators
        var called = Regex.Matches(cppTakeLast + cppUnfusedSelector, @"StackLinq::(\w+)\(")
            .Select(static m => m.Groups[1].Value)
            .Distinct()
            .ToArray();
#pragma warning restore RS0030
        Assert.Contains("TakeLast", called);
        Assert.Contains("SkipLast", called);
        Assert.Contains("Sum", called);
        Assert.Contains("LastOrDefault", called);
        Assert.All(called, static name => Assert.Contains(name, StackLinqTranspilerRule.DeviceOperators));
    }

    // #1086: an unfused StackLinq query with a capturing lambda stored in a local is an auto local holding the
    // StackLinq.cuh templates; no C# enumerator struct is defined for it
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void UnfusedStackLinqQuery_CapturingLambdaInLocal_EmitsValidEnumeratorWithoutSizeMismatch()
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
        Assert.Matches(
            @"auto __gsharp_id_\d+_query = Kassyi::ZeroAlloc::Linq::StackLinq::Take\(" +
            @"Kassyi::ZeroAlloc::Linq::StackLinq::Select\(Kassyi::ZeroAlloc::Linq::StackLinq::AsStackEnumerable\(",
            cppSource);
        Assert.Matches(@"Kassyi::ZeroAlloc::Linq::StackLinq::Sum\(__gsharp_id_\d+_query\)", cppSource);
        Assert.DoesNotContain("__gsharp_ty_Kassyi__ZeroAlloc__Linq", cppSource, StringComparison.Ordinal);
        Assert.DoesNotContain(": size\");", cppSource, StringComparison.Ordinal);
    }

    // #1084: Expect outputCompilation.GetDiagnostics() to have zero errors once ContextHoister avoids hoisting non-crossing SharedMemory<T> locals to outer scope.
    [Fact(Skip = "#1084: ContextHoister hoists non-crossing SharedMemory locals to outer scope causing CS0136.")]
    [Scenario(TestScenario.SadPath)]
    public void SharedMemorySiblingScopes_LocalDeclarations_CompilesWithoutCS0136InManagedCSharp()
    {
        var source = RoslynTestUtility.ReadTestCase(
            "Transpiler/PendingBugs/SharedMemorySiblingScopeHoistKernels.cu.cs");

        var (_, outputCompilation, _) = RoslynTestUtility.RunGenerator(source, "Cpu");
        var compilerErrors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id == "CS0136")
            .ToArray();

        Assert.Empty(compilerErrors);
    }

    // #1085: Expect C++ declarations for parenthesized creation `s` and alias copy `alias` to be emitted once LocalDeclarationStatementTranspilerRule handles non-direct SharedMemory<T> initializers.
    [Fact(Skip = "#1085: LocalDeclarationStatementTranspilerRule omits C++ variable declarations for non-direct SharedMemory initializers.")]
    [Scenario(TestScenario.SadPath)]
    public void SharedMemoryLocal_ParenthesizedAndAliasForms_EmitsCppVariableDeclarations()
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
        Assert.Contains("int* __gsharp_id_1_s", cppSource, StringComparison.Ordinal);
        Assert.Contains("int* __gsharp_id_2_alias", cppSource, StringComparison.Ordinal);
    }

    // #1085: Expect C++ declaration and consistent type for `returned = IdentitySharedMemory(localSmem)` once SharedMemory<T> local declarations and CPU representation are unified.
    [Fact(Skip = "#1085: Helper returning SharedMemory omits returned local declaration in CPU dialect.")]
    [Scenario(TestScenario.SadPath)]
    public void SharedMemoryLocal_PassToHelperFunction_EmitsReturnedLocalDeclarationInCpuDialect()
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
        Assert.Contains("__gsharp_id_2_returned =", cppSource, StringComparison.Ordinal);
    }

    // #543: Expect distinct site keys (Assert.NotEqual) for AliasInStruct<int> and AliasInStruct<float> once SharedMemorySiteKey includes constructed struct type arguments.
    [Fact(Skip = "#543: SharedMemorySiteKey collides across type arguments in generic structs.")]
    [Scenario(TestScenario.SadPath)]
    public void SharedMemoryLocal_TypeAliasInGenericStruct_EmitsDistinctSiteKeysAcrossTypeArguments()
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

#pragma warning disable RS0030 // Generated C++ has no syntax tree; compares the hash site keys of two allocations
        var siteKeys = Regex.Matches(cppSource, @"__gsharp_allocate_smem_static\((0x[0-9a-fA-F]+ull)")
            .Select(m => m.Groups[1].Value)
            .ToArray();
#pragma warning restore RS0030

        Assert.Equal(2, siteKeys.Length);
        Assert.NotEqual(siteKeys[0], siteKeys[1]);
    }

    // #543: Expect 4 distinct site keys for generic struct constructors/operators and valid site key computation in property getters once SharedMemorySiteKey supports them.
    [Fact(Skip = "#543: Generic struct constructors, operators, and property getters fail SharedMemorySiteKey computation.")]
    [Scenario(TestScenario.SadPath)]
    public void SharedMemoryLocal_GenericStructMembers_EmitsDistinctSiteKeysWithoutGetterException()
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

#pragma warning disable RS0030 // Generated C++ has no syntax tree; compares the hash site keys of two allocations
        var ctorAndOpKeys = Regex.Matches(cppCtorAndOp, @"__gsharp_allocate_smem_static\((0x[0-9a-fA-F]+ull)")
            .Select(m => m.Groups[1].Value)
            .ToArray();
#pragma warning restore RS0030

        Assert.Equal(2, ctorAndOpKeys.Length);
        Assert.Equal(2, ctorAndOpKeys.Distinct(StringComparer.Ordinal).Count());

        var computedPropMethod = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "UseComputedPropSharedMemoryBufKernel");

        var failures2 = new List<TranspileFailure>();
        var cppComputedProp = MethodTranspiler.GenerateCudaSource(
            computedPropMethod, model, failures2.Add, isDevice: false, new StandardCppDialect(), out _);
        Assert.NotNull(cppComputedProp);
    }
}
