#nullable enable
namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Tests for the parts of <c>fuzz-diff compare</c> that need no build: header stripping, splitting a launcher file by
/// kernel, the difference records, and option parsing.
/// </summary>
public class CodegenComparisonTests
{
    private static readonly string s_launcherFile = TestCaseFiles.Read("Compare", "LauncherFile.cs");
    private static readonly string s_nativeModule = TestCaseFiles.Read("Compare", "NativeModule.cpp");

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void StripSymbolHeader_ModuleWithHashLine_DropsHashLineBomAndCarriageReturns()
    {
        string body = CodegenComparison.StripSymbolHeader("﻿// gsharp-symbols: 0123\r\n#include <x>\r\nint a;\r\n", out bool hadHeader);

        // mutant: StripSymbolHeader keeps the first line
        // input: a module with a byte order mark, the hash line and CRLF line endings
        Assert.True(hadHeader);
        Assert.Equal("#include <x>\nint a;\n", body);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void StripSymbolHeader_ModuleWithoutHashLine_KeepsFirstLine()
    {
        string body = CodegenComparison.StripSymbolHeader("#include <x>\nint a;", out bool hadHeader);

        Assert.False(hadHeader);
        Assert.Equal("#include <x>\nint a;", body);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void SplitByKernel_LauncherFile_AssignsMembersToThePrecedingSourceConstant()
    {
        var segments = CodegenComparison.SplitByKernel(s_launcherFile);

        // Members before the first `_Source` constant (AotContexts) belong to no kernel; the nested context class and
        // the state machine after `Kernel_00_Source` belong to Kernel_00.
        // mutant: SplitByKernel does not reset the current kernel at the next `_Source` constant
        // input: a launcher file with Kernel_00 and Kernel_01
        Assert.Equal(2, segments.NativeSources.Count);
        Assert.True(segments.NativeSources.ContainsKey("Kernel_00"));
        Assert.Equal("__global__ void Kernel_01(int* a)\n{\na[0]=2;\n}", segments.NativeSources["Kernel_01"]);
        Assert.Contains("__gsharp_context_FuzzBatchKernels__Kernel_u00", segments.ManagedMembers["Kernel_00"], StringComparison.Ordinal);
        Assert.DoesNotContain("Kernel_u01", segments.ManagedMembers["Kernel_00"], StringComparison.Ordinal);
        Assert.DoesNotContain("AotContexts", segments.ManagedMembers["Kernel_00"], StringComparison.Ordinal);
        Assert.DoesNotContain("__global__", segments.ManagedMembers["Kernel_01"], StringComparison.Ordinal);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void FindFirstLineDifference_DifferentLine_ReturnsItsNumberAndBothTexts()
    {
        var diff = CodegenComparison.FindFirstLineDifference("a\nb\nc", "a\r\nB\r\nc");

        Assert.Equal((2, "b", "B"), diff);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void FindFirstLineDifference_OneTextLonger_ReportsEndOfFile()
    {
        var diff = CodegenComparison.FindFirstLineDifference("a\nb", "a\nb\nc");

        Assert.Equal((3, "<EOF>", "c"), diff);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void FindFirstLineDifference_EqualTextsWithDifferentLineEndings_ReturnsNull()
    {
        Assert.Null(CodegenComparison.FindFirstLineDifference("a\nb\n", "a\r\nb\r\n"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Compare_OnlySymbolHashDiffers_ReportsNothing()
    {
        var baseOutputs = Outputs(s_nativeModule, s_launcherFile);
        var headOutputs = Outputs(s_nativeModule.Replace("0123456789abcdef", "fedcba9876543210", StringComparison.Ordinal), s_launcherFile);

        // mutant: Compare does not strip the `// gsharp-symbols:` line
        // input: two modules that differ only in the symbol hash
        Assert.Empty(CodegenComparison.Compare("Cpu", baseOutputs, headOutputs));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Compare_KernelNativeCodeChanged_ReportsThatKernelOnlyInTheNativeOutput()
    {
        var baseOutputs = Outputs(s_nativeModule, s_launcherFile);
        var headOutputs = Outputs(
            s_nativeModule.Replace("a[0]=2;", "a[0]=3;", StringComparison.Ordinal),
            s_launcherFile.Replace("a[0]=2;", "a[0]=3;", StringComparison.Ordinal));

        var differences = CodegenComparison.Compare("Cuda", baseOutputs, headOutputs);

        // The `_Source` constant in the launcher file changed with the module; that is the native difference, not a
        // second, managed one.
        // mutant: CompareManaged reports a file difference when only the native code constants differ
        // input: Kernel_01's native code changed in the module and in its `_Source` constant
        var difference = Assert.Single(differences);
        Assert.Equal(
            new CodegenDifference("Kernel_01", "Cuda", CodegenComparison.NativeOutput, "kernel", 3, 9, "a[0]=2;", "a[0]=3;"),
            difference);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Compare_ManagedKernelBodyChanged_ReportsThatKernelInTheLauncherFile()
    {
        var baseOutputs = Outputs(s_nativeModule, s_launcherFile);
        var headOutputs = Outputs(s_nativeModule, s_launcherFile.Replace("a[0] = 2;", "a[0] = 4;", StringComparison.Ordinal));

        var differences = CodegenComparison.Compare("Cpu", baseOutputs, headOutputs);

        // mutant: Compare compares only the native module
        // input: the managed state machine of Kernel_01 changed, its native code did not
        var difference = Assert.Single(differences);
        Assert.Equal("Kernel_01", difference.Kernel);
        Assert.Equal("Launchers.g.cs", difference.Output);
        Assert.Equal("kernel", difference.Scope);
        Assert.Equal(("a[0] = 2;", "a[0] = 4;"), (difference.BaseText, difference.HeadText));
        Assert.Equal(
            "a[0] = 4;",
            headOutputs.ManagedFiles["Launchers.g.cs"].Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[difference.FileLine - 1]);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Compare_NativeCodeOutsideKernelsChanged_ReportsModuleScope()
    {
        var baseOutputs = Outputs(s_nativeModule, s_launcherFile);
        var headOutputs = Outputs(s_nativeModule.Replace("#include <cstdint>", "#include <stdint.h>", StringComparison.Ordinal), s_launcherFile);

        var difference = Assert.Single(CodegenComparison.Compare("Cpu", baseOutputs, headOutputs));

        // mutant: CompareNative returns without a module difference when no kernel's code differs
        // input: a changed include line before every kernel
        Assert.Equal(
            new CodegenDifference(null, "Cpu", CodegenComparison.NativeOutput, "module", 1, 2, "#include <cstdint>", "#include <stdint.h>"),
            difference);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Compare_GeneratedFileWithoutKernelsChanged_ReportsFileScope()
    {
        var baseOutputs = Outputs(s_nativeModule, s_launcherFile, ("GSharpSharedCpuPrelude.g.cs", "class P\n{\nint a;\n}\n"));
        var headOutputs = Outputs(s_nativeModule, s_launcherFile, ("GSharpSharedCpuPrelude.g.cs", "class P\n{\nlong a;\n}\n"));

        var difference = Assert.Single(CodegenComparison.Compare("Cpu", baseOutputs, headOutputs));

        // mutant: CompareManaged skips files that have no `_Source` constant
        // input: a generated prelude file that changed
        Assert.Equal(
            new CodegenDifference(null, "Cpu", "GSharpSharedCpuPrelude.g.cs", "file", 3, 3, "int a;", "long a;"),
            difference);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Compare_GeneratedFileOnlyInWorkingTree_ReportsFileScope()
    {
        var baseOutputs = Outputs(s_nativeModule, s_launcherFile);
        var headOutputs = Outputs(s_nativeModule, s_launcherFile, ("X.DeviceStateMachines.g.cs", "class S\n{\n}\n"));

        var difference = Assert.Single(CodegenComparison.Compare("Cpu", baseOutputs, headOutputs));

        Assert.Equal(("X.DeviceStateMachines.g.cs", "file", "<missing>", "class S"), (difference.Output, difference.Scope, difference.BaseText, difference.HeadText));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GetSignature_SameChangeInDifferentKernels_IsEqual()
    {
        var first = new CodegenDifference("Kernel_03", "Cpu", "native", "kernel", 5, 40, "call(Kernel_03);", "call(Kernel_03, 1);");
        var second = first with { Kernel = "Kernel_11", Line = 9, BaseText = "call(Kernel_u11);", HeadText = "call(Kernel_u11, 1);" };
        var shiftA = first with { BaseText = "y=x_v2<<(inC[gid]);", HeadText = "y=x_v2<< (inC[gid]);" };
        var shiftB = first with { Kernel = "Kernel_07", BaseText = "z=(long)(a_v12<<(inA[i]))+1;", HeadText = "z=(long)(a_v12<< (inA[i]))+1;" };

        // Equal: the same edit next to a plain and an encoded kernel name, and the same space after `<<` in two lines
        // that differ elsewhere. Not equal: a different edit.
        // mutant: MaskKernelNames leaves the `u` of the encoded name; GetSignature keys on the whole lines
        // input: `call(Kernel_03)` and `call(Kernel_u11)` gaining `, 1`; two lines gaining a space after `<<`
        Assert.Equal(CodegenComparison.GetSignature(first), CodegenComparison.GetSignature(second));
        Assert.Equal(CodegenComparison.GetSignature(shiftA), CodegenComparison.GetSignature(shiftB));
        Assert.NotEqual(CodegenComparison.GetSignature(first), CodegenComparison.GetSignature(first with { HeadText = "call(Kernel_03, x);" }));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void TryParseOptions_WithoutBase_Fails()
    {
        bool parsed = CompareCommand.TryParseOptions(["--batches", "2"], out var options, out _, out string? error);

        Assert.False(parsed);
        Assert.Null(options);
        Assert.Equal("--base <ref> is required.", error);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void TryParseOptions_AllOptions_AreRead()
    {
        bool parsed = CompareCommand.TryParseOptions(
            ["--base", "origin/main", "--batches", "2", "--batch-size", "16", "--fuzz-seed", "0x10", "--dialects", "cuda",
             "--metamorphic", "--expect-change", "--no-reduce", "--reduce-minutes", "5"],
            out var options,
            out _,
            out _);

        Assert.True(parsed);
        Assert.NotNull(options);
        Assert.Equal(("origin/main", 2, 16, 0x10UL), (options.BaseRef, options.Batches, options.Generation.BatchSize, options.Generation.FuzzSeed));
        Assert.Equal(["Cuda"], options.Dialects);
        Assert.True(options.Generation.Metamorphic && options.ExpectChange && options.NoReduce);
        Assert.Equal(5, options.ReduceMinutes);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void TryParseOptions_UnknownDialect_Fails()
    {
        Assert.False(CompareCommand.TryParseOptions(["--base", "main", "--dialects", "Cpu,Metal"], out _, out _, out _));
    }

    private static GeneratorOutputs Outputs(string nativeModule, string launcherFile, params (string Name, string Text)[] otherFiles)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal) { ["Launchers.g.cs"] = launcherFile };
        foreach (var (name, text) in otherFiles)
        {
            files[name] = text;
        }

        return new GeneratorOutputs(nativeModule, files);
    }
}
