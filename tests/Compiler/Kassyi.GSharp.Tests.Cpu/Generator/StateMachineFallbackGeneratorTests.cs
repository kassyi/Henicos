#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Cpu.Generator;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class StateMachineFallbackGeneratorTests
{
    private static readonly string s_casesSource = ReadCase("Generator", "Cases", "StateMachineFallbackCases.cs");

    private static readonly Lazy<Compilation> s_casesCompilation =
        new(static () => RoslynTestUtility.CreateCompilation(s_casesSource).WithAssemblyName("FallbackCasesTest"));

    // A new obstacle without a reason would throw in the translator and drop the method's code from the build
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ForNative_EveryObstacle_HasAReasonNamedAfterIt()
    {
        foreach (var obstacle in Enum.GetValues<NativeStackObstacle>())
        {
            var reason = StateMachineReason.ForNative(obstacle);
            if (obstacle is NativeStackObstacle.None or NativeStackObstacle.NoBlockBarrier)
            {
                Assert.Null(reason);
                continue;
            }

            Assert.Equal(obstacle.ToString(), Assert.NotNull(reason).Name);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StateMachineFallbacks_DefaultOptionsAndCudaWithNativeStack_EmitNoFile()
    {
        var defaultCpu = RunGenerator(s_casesCompilation.Value, "Cpu", nativeModel: null, managedModel: null);
        Assert.DoesNotContain(defaultCpu, static s => s.HintName == StateMachineFallbackPipeline.HintName);

        var explicitStateMachine = RunGenerator(
            s_casesCompilation.Value, "Cpu", nativeModel: "StateMachine", managedModel: "StateMachine");
        Assert.DoesNotContain(explicitStateMachine, static s => s.HintName == StateMachineFallbackPipeline.HintName);

        var cudaWithNativeStack = RunGenerator(
            s_casesCompilation.Value, "Cuda", nativeModel: "Stack", managedModel: null);
        Assert.DoesNotContain(cudaWithNativeStack, static s => s.HintName == StateMachineFallbackPipeline.HintName);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StateMachineFallbacks_ZeroFallbackCompilation_EmitsNoFile()
    {
        const string Source = """
            using Kassyi.GSharp.Core;

            namespace ZeroFallbackCases;

            public static partial class CleanKernels
            {
                [GpuDeviceFunction]
                public static int NonSuspending(int x) => x + 1;

                [GpuDeviceFunction]
                public static void EligibleHelper(int[] data)
                {
                    Gpu.SyncThreads();
                    data[ThreadIndex.X] += 1;
                }

                [GpuKernel]
                public static void NoBarrierKernel(int[] data)
                {
                    data[ThreadIndex.X] = NonSuspending(data[ThreadIndex.X]);
                }

                [GpuKernel]
                public static void EligibleKernel(int[] data)
                {
                    EligibleHelper(data);
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(Source).WithAssemblyName("ZeroFallbackTest");
        var sources = RunGenerator(compilation, "Cpu", nativeModel: "Stack", managedModel: "Stack");
        Assert.DoesNotContain(sources, static s => s.HintName == StateMachineFallbackPipeline.HintName);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("FallbackCases.GridSyncKernel", NativeStackObstacle.GridSync, "uses Gpu.SyncGrid()")]
    [InlineData("FallbackCases.GridSyncHelper", NativeStackObstacle.GridSync, "uses Gpu.SyncGrid()")]
    [InlineData("FallbackCases.CooperativeKernel", NativeStackObstacle.Cooperative, "cooperative")]
    [InlineData("FallbackCases.RecursiveCallKernel", NativeStackObstacle.RecursiveSuspendingCall, "recursive")]
    [InlineData("FallbackCases.RecursiveWrapperHelper", NativeStackObstacle.RecursiveSuspendingCall, "recursive")]
    [InlineData("FallbackCases.ShuffleCallingKernelKernel", NativeStackObstacle.WarpLayout, "visit counts")]
    [InlineData("FallbackCases.FloatSwitchNaNKernel", NativeStackObstacle.CaseValueWithoutLabel, "case value")]
    [InlineData("FallbackCases.WarpOperatorMacroKernel", NativeStackObstacle.WarpOperatorMacro, "operator macro")]
    [InlineData("FallbackCases.StackBodyLoweringKernel", NativeStackObstacle.StackBodyLowering, "lowered")]
    [InlineData("FallbackCases.StructCtorBarrierKernel", NativeStackObstacle.StructMemberSplit, "struct member")]
    [InlineData("FallbackCases.StructCtorDeviceHelper", NativeStackObstacle.StructMemberSplit, "struct member")]
    [InlineData("FallbackCases.GridSyncStructCtorKernel", NativeStackObstacle.StructMemberSplit, "struct member")]
    [InlineData("FallbackCases.GridSyncStructCtorDeviceHelper", NativeStackObstacle.StructMemberSplit, "struct member")]
    [InlineData("FallbackCases.CallsGridSyncStructCtorDeviceKernel", NativeStackObstacle.StructMemberSplit, "struct member")]
    internal void StateMachineFallbacks_NativeObstacle_EmitsLineAndReason(
        string displayName,
        NativeStackObstacle obstacle,
        string expectedSentenceFragment)
    {
        var sources = RunGenerator(s_casesCompilation.Value, "Cpu", nativeModel: "Stack", managedModel: null);
        var file = Assert.Single(sources, static s => s.HintName == StateMachineFallbackPipeline.HintName);

        var reason = StateMachineReason.ForNative(obstacle);
        Assert.NotNull(reason);
        Assert.Equal(CpuPath.Native, reason.Value.Path);
        Assert.Equal(obstacle.ToString(), reason.Value.Name);
        Assert.Contains(expectedSentenceFragment, reason.Value.Sentence, StringComparison.OrdinalIgnoreCase);

        var expectedLine = $"// native\t{displayName}\t{reason.Value.Name}";
        Assert.Contains(expectedLine, file.SourceText, StringComparison.Ordinal);

        Assert.DoesNotContain("EligibleBarrierKernel", file.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("EligibleSuspendingHelper", file.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("NoBarrierKernel", file.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("NonSuspendingHelper", file.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("UsingActiveMaskKernel", file.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("UsingActiveMaskHelper", file.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void StateMachineFallbacks_NativeImportedSuspendingCall_EmitsLineAndReason()
    {
        var consumer = CreateImportedConsumerCompilation();
        var sources = RunGenerator(consumer, "Cpu", nativeModel: "Stack", managedModel: null);
        var file = Assert.Single(sources, static s => s.HintName == StateMachineFallbackPipeline.HintName);

        var reason = StateMachineReason.ForNative(NativeStackObstacle.ImportedSuspendingCall);
        Assert.NotNull(reason);
        Assert.Equal("ImportedSuspendingCall", reason.Value.Name);
        Assert.Contains("another assembly", reason.Value.Sentence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("// native\tKernels.CallRemoteKernel\tImportedSuspendingCall", file.SourceText,
            StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void StateMachineFallbacks_WarpFrameTooLarge_EmitsNativeAndManagedLinesAndReasons()
    {
        var compilation = CreateWarpFrameTooLargeCompilation();

        var nativeSources = RunGenerator(compilation, "Cpu", nativeModel: "Stack", managedModel: null);
        var nativeFile = Assert.Single(nativeSources, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        var nativeReason = StateMachineReason.ForNative(NativeStackObstacle.WarpFrameTooLarge);
        Assert.NotNull(nativeReason);
        Assert.Equal("WarpFrameTooLarge", nativeReason.Value.Name);
        Assert.Contains("visit counts", nativeReason.Value.Sentence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("// native\tKernels.TooManyShufflesKernel\tWarpFrameTooLarge", nativeFile.SourceText,
            StringComparison.Ordinal);

        var managedSources = RunGenerator(compilation, "Cpu", nativeModel: null, managedModel: "Stack");
        var managedFile = Assert.Single(managedSources, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        Assert.Contains("// managed\tKernels.TooManyShufflesKernel\tWarpFrameTooLarge", managedFile.SourceText,
            StringComparison.Ordinal);

        var kernel = GetMethod(compilation, "WarpLimitCases.Kernels", "TooManyShufflesKernel");
        var managedReason = ManagedStackEligibility.FindKernelReason(kernel, compilation);
        Assert.NotNull(managedReason);
        Assert.Equal("WarpFrameTooLarge", managedReason.Value.Name);
        Assert.Contains("4097 visit counts", managedReason.Value.Sentence, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("FallbackCases", "NonSpanRefStructKernel", "UnsupportedStackArgument", "ref struct")]
    [InlineData("FallbackCases", "RecursiveCallKernel", "RecursiveSuspendingCall", "recursive")]
    [InlineData("FallbackCases", "CallsFileLocalDeviceKernel", "FileLocalType", "file-local")]
    [InlineData("FallbackCases", "CallsInstanceDeviceKernel", "InstanceDeviceFunction", "instance")]
    [InlineData("FallbackCases", "CallsNonPartialDeviceKernel", "NonPartialContainingType", "partial")]
    [InlineData("FallbackCases", "StructCtorBarrierKernel", "StructMemberSplit", "struct member")]
    [InlineData("FallbackCases", "GridSyncStructCtorKernel", "StructMemberSplit", "struct member")]
    [InlineData("FallbackCases", "CallsGridSyncStructCtorDeviceKernel", "StructMemberSplit", "struct member")]
    public void StateMachineFallbacks_ManagedKernelReason_EmitsLineAndReason(
        string typeName,
        string methodName,
        string expectedName,
        string expectedSentenceFragment)
    {
        var compilation = s_casesCompilation.Value;
        var method = GetMethod(compilation, $"StateMachineFallbackGeneratorCases.{typeName}", methodName);
        var reason = ManagedStackEligibility.FindKernelReason(method, compilation);
        Assert.NotNull(reason);
        Assert.Equal(CpuPath.Managed, reason.Value.Path);
        Assert.Equal(expectedName, reason.Value.Name);
        Assert.Contains(expectedSentenceFragment, reason.Value.Sentence, StringComparison.OrdinalIgnoreCase);

        var sources = RunGenerator(compilation, "Cpu", nativeModel: null, managedModel: "Stack");
        var file = Assert.Single(sources, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        Assert.Contains($"// managed\t{typeName}.{methodName}\t{expectedName}", file.SourceText,
            StringComparison.Ordinal);
    }

    // A cooperative kernel and a grid sync run on the managed stack model: no managed reason and no managed line
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("CooperativeKernel", false)]
    [InlineData("CooperativeAndUsingKernel", false)]
    [InlineData("GridSyncKernel", false)]
    [InlineData("GridSyncHelper", true)]
    public void StateMachineFallbacks_ManagedCooperativeOrGridSync_HasNoReason(string methodName, bool isDeviceFunction)
    {
        var compilation = s_casesCompilation.Value;
        var method = GetMethod(compilation, "StateMachineFallbackGeneratorCases.FallbackCases", methodName);
        var reason = isDeviceFunction
            ? ManagedStackEligibility.FindFunctionReason(method, compilation)
            : ManagedStackEligibility.FindKernelReason(method, compilation);
        Assert.Null(reason);

        var sources = RunGenerator(compilation, "Cpu", nativeModel: null, managedModel: "Stack");
        var file = Assert.Single(sources, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        Assert.DoesNotContain($"// managed\tFallbackCases.{methodName}\t", file.SourceText, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("FallbackCases", "DirectlyRecursiveHelper", "RecursiveSuspendingCall", "recursive")]
    [InlineData("FallbackCases", "RecursiveWrapperHelper", "RecursiveSuspendingCall", "recursive")]
    [InlineData("FallbackCases", "FileLocalDeviceHelper", "FileLocalType", "file-local")]
    [InlineData("InstanceDeviceHolder", "InstanceSuspendingHelper", "InstanceDeviceFunction", "instance")]
    [InlineData("NonPartialHolder", "NonPartialSuspendingHelper", "NonPartialContainingType", "partial")]
    [InlineData("FallbackCases", "StructCtorDeviceHelper", "StructMemberSplit", "struct member")]
    [InlineData("FallbackCases", "GridSyncStructCtorDeviceHelper", "StructMemberSplit", "struct member")]
    public void StateMachineFallbacks_ManagedSuspendingDeviceReason_EmitsLineAndReason(
        string typeName,
        string methodName,
        string expectedName,
        string expectedSentenceFragment)
    {
        var compilation = s_casesCompilation.Value;
        var method = GetMethod(compilation, $"StateMachineFallbackGeneratorCases.{typeName}", methodName);
        var reason = ManagedStackEligibility.FindFunctionReason(method, compilation);
        Assert.NotNull(reason);
        Assert.Equal(CpuPath.Managed, reason.Value.Path);
        Assert.Equal(expectedName, reason.Value.Name);
        Assert.Contains(expectedSentenceFragment, reason.Value.Sentence, StringComparison.OrdinalIgnoreCase);

        var sources = RunGenerator(compilation, "Cpu", nativeModel: null, managedModel: "Stack");
        var file = Assert.Single(sources, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        Assert.Contains($"// managed\t{typeName}.{methodName}\t{expectedName}", file.SourceText,
            StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void StateMachineFallbacks_ManagedImportedAndWarpLayoutAndFormFailures_EmitExpectedLines()
    {
        var consumer = CreateImportedConsumerCompilation();
        var importedSources = RunGenerator(consumer, "Cpu", nativeModel: null, managedModel: "Stack");
        var importedFile = Assert.Single(importedSources, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        Assert.Contains("// managed\tKernels.CallRemoteKernel\tWarpLayout", importedFile.SourceText,
            StringComparison.Ordinal);

        var importedKernel = GetMethod(consumer, "ConsumerApp.Kernels", "CallRemoteKernel");
        var importedKernelReason = ManagedStackEligibility.FindKernelReason(importedKernel, consumer);
        Assert.NotNull(importedKernelReason);
        Assert.Equal("WarpLayout", importedKernelReason.Value.Name);
        Assert.Contains("visit counts", importedKernelReason.Value.Sentence, StringComparison.OrdinalIgnoreCase);

        var remoteCallee = GetMethod(consumer, "ImportedStackLib.Helpers", "RemoteBarrier");
        var remoteCalleeReason = ManagedStackEligibility.FindFunctionReason(remoteCallee, consumer);
        Assert.NotNull(remoteCalleeReason);
        Assert.Equal("ImportedSuspendingCall", remoteCalleeReason.Value.Name);
        Assert.Contains("another assembly", remoteCalleeReason.Value.Sentence, StringComparison.OrdinalIgnoreCase);

        var casesSources = RunGenerator(s_casesCompilation.Value, "Cpu", nativeModel: null, managedModel: "Stack");
        var casesFile = Assert.Single(casesSources, static s => s.HintName == StateMachineFallbackPipeline.HintName);
        Assert.Contains("// managed\tFallbackCases.ShuffleCallingKernelKernel\tWarpLayout", casesFile.SourceText,
            StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackCases.MacroOperatorDeviceHelper\tStackFormGenerationFailed: InvalidOperationException",
            casesFile.SourceText, StringComparison.Ordinal);
        Assert.Contains("// managed\tFallbackCases.CallsMacroOperatorDeviceKernel\tCallsFunctionWithoutForm: calls a function without a stack form",
            casesFile.SourceText, StringComparison.Ordinal);

        // Eligible, non-barrier, non-suspending, and ManagedMode != Copy kernels must not be listed
        Assert.DoesNotContain("EligibleBarrierKernel", casesFile.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("NoBarrierKernel", casesFile.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("NonSuspendingHelper", casesFile.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("FileLocalUnavailableKernel", casesFile.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("UsingActiveMaskKernel", casesFile.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("UsingActiveMaskHelper", casesFile.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StateMachineReason_DirectReasonQueries_PinNamesAndSentences()
    {
        Assert.Null(StateMachineReason.ForNative(NativeStackObstacle.None));
        Assert.Null(StateMachineReason.ForNative(NativeStackObstacle.NoBlockBarrier));

        var casesCompilation = s_casesCompilation.Value;
        var noBarrierKernel = GetMethod(
            casesCompilation, "StateMachineFallbackGeneratorCases.FallbackCases", "NoBarrierKernel");
        var noBarrierKernelReason = ManagedStackEligibility.FindKernelReason(noBarrierKernel, casesCompilation);
        Assert.NotNull(noBarrierKernelReason);
        Assert.Equal(CpuPath.Managed, noBarrierKernelReason.Value.Path);
        Assert.Equal("NoBlockBarrier", noBarrierKernelReason.Value.Name);
        Assert.Contains("does not reach a barrier or warp collective", noBarrierKernelReason.Value.Sentence,
            StringComparison.Ordinal);

        var nonSuspendingHelper = GetMethod(
            casesCompilation, "StateMachineFallbackGeneratorCases.FallbackCases", "NonSuspendingHelper");
        var noBarrierFunctionReason = ManagedStackEligibility.FindFunctionReason(
            nonSuspendingHelper, casesCompilation);
        Assert.NotNull(noBarrierFunctionReason);
        Assert.Equal(CpuPath.Managed, noBarrierFunctionReason.Value.Path);
        Assert.Equal("NoBlockBarrier", noBarrierFunctionReason.Value.Name);
        Assert.Contains("does not reach a barrier or warp collective", noBarrierFunctionReason.Value.Sentence,
            StringComparison.Ordinal);

        const string DirectSource = """
            using Kassyi.GSharp.Core;

            namespace DirectReasonCases;

            public partial class InstanceKernels
            {
                [GpuKernel]
                public void InstanceKernel(int[] data)
                {
                    Gpu.SyncThreads();
                    data[ThreadIndex.X] = 1;
                }
            }

            public static partial class StaticKernels
            {
                static partial void BodylessKernel(int[] data);

                [GpuKernel]
                public static void ShuffleCalleeKernel(int[] data)
                {
                    data[ThreadIndex.X] = Warp.ShuffleXorSync(0xFFFFFFFFu, ThreadIndex.X, 1);
                }

                [GpuDeviceFunction]
                public static void ShuffleCallingDevice(int[] data)
                {
                    ShuffleCalleeKernel(data);
                    data[0] = Warp.ShuffleXorSync(0xFFFFFFFFu, data[0], 1);
                }
            }
            """;

        var directCompilation = RoslynTestUtility.CreateCompilation(DirectSource).WithAssemblyName("DirectReasonTest");

        var instanceReason = ManagedStackEligibility.FindKernelReason(
            GetMethod(directCompilation, "DirectReasonCases.InstanceKernels", "InstanceKernel"), directCompilation);
        Assert.NotNull(instanceReason);
        Assert.Equal(CpuPath.Managed, instanceReason.Value.Path);
        Assert.Equal("InstanceKernel", instanceReason.Value.Name);
        Assert.Contains("instance kernel", instanceReason.Value.Sentence, StringComparison.Ordinal);

        var noBodyReason = ManagedStackEligibility.FindKernelReason(
            GetMethod(directCompilation, "DirectReasonCases.StaticKernels", "BodylessKernel"), directCompilation);
        Assert.NotNull(noBodyReason);
        Assert.Equal(CpuPath.Managed, noBodyReason.Value.Path);
        Assert.Equal("NoMethodBody", noBodyReason.Value.Name);
        Assert.Contains("not in this compilation", noBodyReason.Value.Sentence, StringComparison.Ordinal);

        var warpDeviceReason = ManagedStackEligibility.FindFunctionReason(
            GetMethod(directCompilation, "DirectReasonCases.StaticKernels", "ShuffleCallingDevice"), directCompilation);
        Assert.NotNull(warpDeviceReason);
        Assert.Equal(CpuPath.Managed, warpDeviceReason.Value.Path);
        Assert.Equal("WarpLayout", warpDeviceReason.Value.Name);
        Assert.Contains("visit counts", warpDeviceReason.Value.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void StateMachineFallbacks_BothSwitchesActive_SortsDeterministicallyAndMatchesSourceConstant()
    {
        var sources = RunGenerator(s_casesCompilation.Value, "Cpu", nativeModel: "Stack", managedModel: "Stack");
        var file = Assert.Single(sources, static s => s.HintName == StateMachineFallbackPipeline.HintName);

        var commentLines = new List<string>();
        foreach (var rawLine in file.SourceText.AsSpan().EnumerateLines())
        {
            if (rawLine.Length > 0 && char.IsWhiteSpace(rawLine[0]))
            {
                Assert.Fail($"Generated line is indented: {rawLine}");
            }

            if (rawLine.StartsWith("// managed\t", StringComparison.Ordinal) ||
                rawLine.StartsWith("// native\t", StringComparison.Ordinal))
            {
                commentLines.Add(rawLine[3..].ToString());
            }
        }

        Assert.Contains(commentLines, static l => l.StartsWith("managed\t", StringComparison.Ordinal));
        Assert.Contains(commentLines, static l => l.StartsWith("native\t", StringComparison.Ordinal));

        for (var i = 1; i < commentLines.Count; i++)
        {
            Assert.True(
                CompareThreeColumns(commentLines[i - 1], commentLines[i]) <= 0,
                $"Lines out of order at {i}: '{commentLines[i - 1]}' before '{commentLines[i]}'.");
        }

        // Verify parity between comment lines and GSharpStateMachineFallbacks.Source
        var compilation = s_casesCompilation.Value;
        var driver = CSharpGeneratorDriver.Create(
                generators: [new GpuKernelGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                optionsProvider: new TestAnalyzerConfigOptionsProvider("Cpu", new Dictionary<string, string>
                {
                    ["build_property.GSharpNativeBarrierModel"] = "Stack",
                    ["build_property.GSharpManagedBarrierModel"] = "Stack"
                }))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        _ = driver;

        var constant = output.SyntaxTrees
            .Single(static t => Path.GetFileName(t.FilePath) == StateMachineFallbackPipeline.HintName)
            .GetRoot()
            .DescendantNodes()
            .OfType<FieldDeclarationSyntax>()
            .Single()
            .Declaration.Variables.Single()
            .Initializer!.Value; // Test asserts generated Source field has a literal initializer
        var literal = Assert.IsType<LiteralExpressionSyntax>(constant);
        var sourceValue = Assert.IsType<string>(literal.Token.Value);

        var sourceLines = new List<string>();
        foreach (var line in sourceValue.AsSpan().EnumerateLines())
        {
            if (!line.IsEmpty)
            {
                sourceLines.Add(line.ToString());
            }
        }

        Assert.Equal(commentLines, sourceLines);
    }

    private static int CompareThreeColumns(string left, string right)
    {
        var (lKind, lName, lReason) = SplitThree(left.AsSpan());
        var (rKind, rName, rReason) = SplitThree(right.AsSpan());

        var kindCmp = StringComparer.Ordinal.Compare(lKind, rKind);
        if (kindCmp != 0)
        {
            return kindCmp;
        }

        var nameCmp = StringComparer.Ordinal.Compare(lName, rName);
        return nameCmp != 0 ? nameCmp : StringComparer.Ordinal.Compare(lReason, rReason);
    }

    private static (string Kind, string Name, string Reason) SplitThree(ReadOnlySpan<char> line)
    {
        var count = 0;
        var col0 = string.Empty;
        var col1 = string.Empty;
        var col2 = string.Empty;
        foreach (var part in line.Split('\t'))
        {
            switch (count)
            {
                case 0:
                    col0 = line[part].ToString();
                    break;
                case 1:
                    col1 = line[part].ToString();
                    break;
                case 2:
                    col2 = line[part].ToString();
                    break;
            }

            count++;
        }

        Assert.Equal(3, count);
        return (col0, col1, col2);
    }

    private static Compilation CreateImportedConsumerCompilation()
    {
        var libCompilation = RoslynTestUtility.CreateCompilation(
                ReadCase("Analysis", "Cases", "NativeStackImportedLibraryCases.cs"))
            .WithAssemblyName("ImportedStackLib");
        var (_, libOutput, _) = GeneratorTestRunner.RunGenerator(libCompilation, "Cpu");
        return RoslynTestUtility.CreateCompilation(
                ReadCase("Analysis", "Cases", "NativeStackImportedCallRemoteCases.cs"))
            .WithAssemblyName("ConsumerApp")
            .AddReferences(libOutput.ToMetadataReference());
    }

    private static Compilation CreateWarpFrameTooLargeCompilation()
    {
        const string Shuffle = "        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);\n";
        var shuffles = string.Concat(Enumerable.Repeat(Shuffle, WarpFrameLayout.MaxSize));
        var source = $$"""
            using Kassyi.GSharp.Core;

            namespace WarpLimitCases;

            public static partial class Kernels
            {
                [GpuKernel]
                public static void TooManyShufflesKernel(int[] data)
                {
                    int v = data[ThreadIndex.X];
            {{shuffles}}
                    data[ThreadIndex.X] = v;
                }
            }
            """;
        return RoslynTestUtility.CreateCompilation(source).WithAssemblyName("WarpLimitTest");
    }

    private static (string HintName, string SourceText)[] RunGenerator(
        Compilation compilation,
        string dialect,
        string? nativeModel,
        string? managedModel)
    {
        var options = new Dictionary<string, string>();
        if (nativeModel is not null)
        {
            options["build_property.GSharpNativeBarrierModel"] = nativeModel;
        }

        if (managedModel is not null)
        {
            options["build_property.GSharpManagedBarrierModel"] = managedModel;
        }

        var (driver, _, diagnostics) = GeneratorTestRunner.RunGenerator(
            compilation,
            dialect,
            options.Count > 0 ? options : null);
        Assert.Empty(diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);

        return result.GeneratedSources
            .OrderBy(static s => s.HintName, StringComparer.Ordinal)
            .Select(static s => (s.HintName, s.SourceText.ToString()))
            .ToArray();
    }

    private static IMethodSymbol GetMethod(Compilation compilation, string typeName, string methodName)
    {
        var type = compilation.GetTypeByMetadataName(typeName)
            ?? throw new InvalidOperationException($"Type '{typeName}' was not found.");
        return type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
    }

    private static string ReadCase(params string[] parts) =>
        File.ReadAllText(RepositoryPaths.Combine(["tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", .. parts]));
}
