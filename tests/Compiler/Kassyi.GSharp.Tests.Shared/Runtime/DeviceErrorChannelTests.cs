using System.Collections.Immutable;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Naming;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Verifies the device runtime error channel and CPU/GPU exception parity (#729).
/// </summary>
public sealed class DeviceErrorChannelTests
{
    public static TheoryData<GSharpErrorCode, Type> AllErrorCodesAndExceptionTypes() =>
        new()
        {
            { GSharpErrorCode.NotSupported, typeof(NotSupportedException) },
            { GSharpErrorCode.InvalidOperation, typeof(InvalidOperationException) },
            { GSharpErrorCode.IndexOutOfRange, typeof(IndexOutOfRangeException) },
            { GSharpErrorCode.ArgumentOutOfRange, typeof(ArgumentOutOfRangeException) },
            { GSharpErrorCode.Argument, typeof(ArgumentException) },
            { GSharpErrorCode.NullReference, typeof(NullReferenceException) },
            { GSharpErrorCode.DivideByZero, typeof(DivideByZeroException) },
            { GSharpErrorCode.Overflow, typeof(OverflowException) },
            { GSharpErrorCode.DataRace, typeof(InvalidOperationException) },
            { GSharpErrorCode.SwitchExpression, typeof(SwitchExpressionException) }
        };

    [Theory]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(AllErrorCodesAndExceptionTypes))]
    public void ErrorMapper_AllErrorCodes_MapsToStandardExceptionsWithMetadata(
        GSharpErrorCode code,
        Type expectedExceptionType)
    {
        var moduleId = GSharpErrorMapper.RegisterErrorSites(
            $"test-module-{code}",
            [
                new GSharpErrorSite(1u, "MyKernels.cu.cs", 42, 13, "MyKernel", $"Custom error for {code}")
            ]);

        var packedThread = GSharpErrorMapper.PackThreadIndex(7, 3, 1);
        var innerCuda = new CudaException(CUresult.CudaErrorIllegalInstruction);

        var slot = new GSharpDeviceErrorSlot
        {
            Code = (uint)code | GSharpErrorMapper.CompletionBit,
            SiteId = 1u,
            ModuleId = moduleId,
            ThreadLinear = packedThread,
            BlockX = 2u,
            BlockYZ = 4u | (1u << 16)
        };

        var ex = GSharpErrorMapper.CreateExceptionFromSlot(in slot, innerCuda);

        Assert.IsType(expectedExceptionType, ex);
        Assert.Same(innerCuda, ex.InnerException);
        Assert.Contains($"Custom error for {code} in MyKernel at MyKernels.cu.cs(42,13) in thread (7,3,1) of block (2,4,1)", ex.Message);
        Assert.Equal(code.ToString(), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("MyKernel", ex.Data["GSharp.MemberName"]);
        Assert.Equal("MyKernels.cu.cs(42,13)", ex.Data["GSharp.SourceLocation"]);
        Assert.Equal("(7,3,1)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(2,4,1)", ex.Data["GSharp.BlockIndex"]);
        Assert.Equal(nameof(CUresult.CudaErrorIllegalInstruction), ex.Data["GSharp.CudaError"]);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void CoordinatePacking_ThreeDimensionalCoordinates_PacksAndUnpacksCoordinates()
    {
        var packedThread = GSharpErrorMapper.PackThreadIndex(511, 15, 3);
        Assert.Equal((511, 15, 3), GSharpErrorMapper.UnpackThreadIndex(packedThread));

        Assert.Equal((4, 9, 3), GSharpErrorMapper.UnpackBlockIndex(4u, 9u | (3u << 16)));
        Assert.Equal((int.MaxValue, 65535, 65535), GSharpErrorMapper.UnpackBlockIndex(0x7FFFFFFFu, 0xFFFFFFFFu));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void ErrorSiteTable_SymbolRegistration_DeduplicatesAndRoundTripsSymbolFile()
    {
        var table = new ErrorSiteTable();
        var id1 = table.Register("Sample.cu.cs", 10, 5, "SampleKernel", "Attempted to divide by zero.");
        var id2 = table.Register("Sample.cu.cs", 20, 9, "HelperMethod", "Index was outside the bounds of the array.");
        var id1Duplicate = table.Register("Sample.cu.cs", 10, 5, "SampleKernel", "Attempted to divide by zero.");

        Assert.True(id1 != 0u);
        Assert.True(id2 != 0u);
        Assert.NotEqual(id1, id2);
        Assert.Equal(id1, id1Duplicate);
        Assert.Equal(2, table.Count);

        using var writer = new SourceWriter();
        ErrorSiteTable.AppendTrapSiteCall(writer, "DivideByZero", id1);
        Assert.Equal($"__gsharp_trap_site(GSharpErrorCode::DivideByZero, {id1}u)", writer.ToString());

        var gsymRecords = new List<GSharpNameEncoding.SymbolErrorSiteRecord>();
        foreach (var entry in table.Entries)
        {
            gsymRecords.Add(new GSharpNameEncoding.SymbolErrorSiteRecord(
                entry.SiteId,
                entry.File,
                entry.Line,
                entry.Column,
                entry.MemberName,
                entry.DefaultMessage));
        }

        const string bodySource = "extern \"C\" __global__ void __gsharp_k_SampleKernel() {}\n";
        var gsymContent = GSharpNameEncoding.BuildSymbolFileContent(bodySource, "Cuda", errorSites: gsymRecords);
        var expectedHash = GSharpNameEncoding.ComputeSourceHash(bodySource);
        var parsed = GSharpNameEncoding.ParseErrorSitesFromSymbolFile(gsymContent, expectedHash);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(new GSharpNameEncoding.SymbolErrorSiteRecord(id1, "Sample.cu.cs", 10, 5, "SampleKernel", "Attempted to divide by zero."), parsed[id1]);
        Assert.Equal(new GSharpNameEncoding.SymbolErrorSiteRecord(id2, "Sample.cu.cs", 20, 9, "HelperMethod", "Index was outside the bounds of the array."), parsed[id2]);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void ErrorSiteTable_BuildModuleSites_DetectsCollisionAndDeduplicates()
    {
        // 1. Deduplication of identical entries
        var entry1 = new ErrorSiteEntry(100u, "Test.cs", 10, 5, "Kernel1", "Error 1");
        var entry1Dup = new ErrorSiteEntry(100u, "Test.cs", 10, 5, "Kernel1", "Error 1");
        var entry2 = new ErrorSiteEntry(200u, "Test.cs", 20, 5, "Kernel2", "Error 2");

        var success = ErrorSiteTable.TryBuildModuleSites(
            [entry1, entry1Dup, entry2],
            out var uniqueSites,
            out var c1,
            out var c2);

        Assert.True(success);
        Assert.Equal(2, uniqueSites.Length);
        Assert.Equal(100u, uniqueSites[0].SiteId);
        Assert.Equal(200u, uniqueSites[1].SiteId);

        // 2. Collision detection when same site ID has different properties
        var entryConflict = new ErrorSiteEntry(100u, "Other.cs", 30, 1, "KernelOther", "Conflicting error");
        var collisionDetected = !ErrorSiteTable.TryBuildModuleSites(
            [entry1, entryConflict],
            out _,
            out var first,
            out var second);

        Assert.True(collisionDetected);
        Assert.Equal(100u, first.SiteId);
        Assert.Equal("Kernel1", first.MemberName);
        Assert.Equal("KernelOther", second.MemberName);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void ErrorSiteTable_VaryingLineEndings_ProducesIdenticalSiteIds()
    {
#pragma warning disable RS0030 // The line endings of the source text are the subject: the same file with LF and with CRLF
        var source = RoslynTestUtility.ReadTestCase("Runtime/ErrorSiteLineEndingCases.cs").Replace("\r\n", "\n");
#pragma warning restore RS0030

        var lf = CreateSitesForTerminals(source);
#pragma warning disable RS0030 // The line endings of the source text are the subject: the same file with LF and with CRLF
        var crlf = CreateSitesForTerminals(source.Replace("\n", "\r\n"));
#pragma warning restore RS0030

        // Three terminals on different lines, each with a real file name, so the comparison below cannot pass vacuously
        Assert.Equal(3, lf.Count);
        Assert.All(lf, static site => Assert.Equal("ErrorSiteLineEndingCases.cs", site.File));
        Assert.Equal(lf.Select(static site => site.SiteId).Distinct().Count(), lf.Count);
        Assert.Equal(lf, crlf);
    }

    private static List<ErrorSiteEntry> CreateSitesForTerminals(string source)
    {
        var compilation = RoslynTestUtility.CreateCompilationAt("ErrorSiteLineEndingCases.cs", source)
            .WithAssemblyName("ErrorSiteLineEndings");
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        return tree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Where(static call => call.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ElementAt" or "First" or "Last"
            })
            .Select(call => ErrorSiteTable.CreateSite(
                call,
                model,
                "SitesOnSeveralLinesKernel",
                "Sequence contains no elements",
                ((Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax)call.Expression).Name.Identifier.ValueText)
            ).ToList();
    }

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckedBuildOption_CheckedAndUncheckedConfigurations_ControlsPostLaunchStreamSynchronization(
        bool isCheckedBuild)
    {
        const string source = """
            using Kassyi.GSharp.Core;

            namespace SampleNamespace;

            public static partial class SampleKernels
            {
                [GpuKernel]
                public static void VoidScalarKernel(int value)
                {
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source)
            .WithAssemblyName("SampleAssembly");

        var optionsProvider = new TestAnalyzerConfigOptionsProvider(
            "Cuda",
            new Dictionary<string, string>
            {
                ["build_property.GSharpCheckedBuild"] = isCheckedBuild ? "true" : "false"
            });

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            optionsProvider: optionsProvider);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var wrapperTree = Assert.Single(runResult.GeneratedTrees, t => t.FilePath.EndsWith("SampleKernels.g.cs", StringComparison.Ordinal));
        var wrapperText = wrapperTree.GetText().ToString();

        // The checked build synchronizes after both launches. In the release build only the managed-array launcher does,
        // since this kernel has no copy-back that would synchronize, so that it reads its own record; the Ptr launcher
        // leaves the error to the next synchronization of the stream.
        const string Synchronize = "cuStreamSynchronize(hStream));";
        Assert.Equal(isCheckedBuild ? 2 : 1, wrapperText.Split(Synchronize).Length - 1);

        // The managed-array launcher reads the device error record of its own launch in every build; the Ptr launcher
        // only where it synchronizes, in the checked build. Both take an epoch for the launch and pass it last; a
        // launcher that reads its record owns the epoch until it does, and releases it in its finally block.
        const string Records = "global::Kassyi.GSharp.Runtime.Aot.LaunchErrorRecords.";
        int readers = isCheckedBuild ? 2 : 1;
        Assert.Equal(readers, Count(wrapperText, $"{Records}ThrowIfLaunchError(__gsharp_epoch);"));
        Assert.Equal(readers, Count(wrapperText, $"{Records}ReleaseLaunchEpoch(__gsharp_epoch);"));
        Assert.DoesNotContain("ThrowIfDeviceError", wrapperText);
        Assert.Equal(readers, Count(wrapperText, $"uint __gsharp_epoch = {Records}NextLaunchEpoch(hStream, true);"));
        Assert.Equal(2 - readers,
            Count(wrapperText, $"uint __gsharp_epoch = {Records}NextLaunchEpoch(hStream, false);"));

        static int Count(string text, string part) => text.Split(part).Length - 1;
    }

#if !GSHARP_CUDA
    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqViolationsOnCpuManagedAndNative_ThrowsStandardExceptions()
    {
        int[] data = [1, 2, 3, 4];
        int[] result = [0];

        // 1. Managed execution
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            Assert.Throws<InvalidOperationException>(() =>
                DeviceErrorChannelKernels.LaunchStackLinqFirstNoMatchKernel(data, 100, result, blockDimX: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DeviceErrorChannelKernels.LaunchStackLinqElementAtKernel(data, 99, result, blockDimX: 1));
        }

        // 2. Native CPU execution
        var firstEx = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqFirstNoMatchKernel(data, 100, result, blockDimX: 1));
        Assert.Contains("Sequence contains no matching element", firstEx.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), firstEx.Data["GSharp.ErrorCode"]);
        Assert.Equal("(0,0,0)", firstEx.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", firstEx.Data["GSharp.BlockIndex"]);

        var elemEx = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqElementAtKernel(data, 99, result, blockDimX: 1));
        Assert.Contains("Index was out of range.", elemEx.Message);
        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), elemEx.Data["GSharp.ErrorCode"]);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_ConcurrentTrapAllThreadsOnCpuNative_FirstWriterWins()
    {
        int[] data = [1, 2, 3, 4];
        int[] result = new int[256];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchConcurrentTrapAllThreadsKernel(data, result, gridDimX: 4, blockDimX: 64));
        Assert.Contains("Sequence contains no matching element", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
        Assert.NotNull(ex.Data["GSharp.ThreadIndex"]);
        Assert.NotNull(ex.Data["GSharp.BlockIndex"]);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_ThreeDimensionalCoordinatesOnCpuNative_CapturedInExceptionData()
    {
        int[] data = [10, 20, 30];
        int[] result = [0];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceErrorChannelKernels.LaunchCoordinate3DTrapKernel(
                data,
                targetTx: 2,
                targetTy: 1,
                targetTz: 1,
                targetBx: 1,
                targetBy: 2,
                targetBz: 1,
                result,
                gridDimX: 3,
                gridDimY: 4,
                gridDimZ: 2,
                blockDimX: 4,
                blockDimY: 3,
                blockDimZ: 2));

        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("(2,1,1)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(1,2,1)", ex.Data["GSharp.BlockIndex"]);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_EndToEndExecutionOnCpuNative_CarriesSourceLocationAndMemberName()
    {
        int[] data = [10, 20, 30];
        int[] result = [0];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqElementAtKernel(data, 999, result, blockDimX: 1));

        Assert.Contains("Index was out of range.", ex.Message);
        Assert.Contains("in StackLinqElementAtKernel at DeviceErrorChannelKernels.cu.cs", ex.Message);
        Assert.Contains("in thread (0,0,0) of block (0,0,0)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("StackLinqElementAtKernel", ex.Data["GSharp.MemberName"]);
        Assert.NotNull(ex.Data["GSharp.SourceLocation"]);
        Assert.StartsWith("DeviceErrorChannelKernels.cu.cs", ex.Data["GSharp.SourceLocation"]?.ToString());
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_GridOneHundredBlocksOnCpuNative_PreservesBlockCoordinate()
    {
        int[] data = [10, 20, 30];
        int[] result = [0];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceErrorChannelKernels.LaunchBlockIndex100TrapKernel(data, targetBlock: 37, result, gridDimX: 100, blockDimX: 32));

        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(37,0,0)", ex.Data["GSharp.BlockIndex"]);
        Assert.Contains("of block (37,0,0)", ex.Message);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqFirstOnEmptySequenceOnCpuNative_CarriesSourceLocationAndMemberName()
    {
        int[] data = [1, 2, 3];
        int[] result = [0];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqFirstEmptyKernel(data, result, blockDimX: 1));

        Assert.Contains("Sequence contains no elements", ex.Message);
        Assert.Contains("in StackLinqFirstEmptyKernel at DeviceErrorChannelKernels.cu.cs", ex.Message);
        Assert.Contains("in thread (0,0,0) of block (0,0,0)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("StackLinqFirstEmptyKernel", ex.Data["GSharp.MemberName"]);
        Assert.NotNull(ex.Data["GSharp.SourceLocation"]);
        Assert.StartsWith("DeviceErrorChannelKernels.cu.cs", ex.Data["GSharp.SourceLocation"]?.ToString());
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqSingleOnMultipleElementsOnCpuNative_CarriesSourceLocationAndMemberName()
    {
        int[] data = [10, 20];
        int[] result = [0];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqSingleMultipleKernel(data, result, blockDimX: 1));

        Assert.Contains("Sequence contains more than one element", ex.Message);
        Assert.Contains("in StackLinqSingleMultipleKernel at DeviceErrorChannelKernels.cu.cs", ex.Message);
        Assert.Contains("in thread (0,0,0) of block (0,0,0)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("StackLinqSingleMultipleKernel", ex.Data["GSharp.MemberName"]);
        Assert.NotNull(ex.Data["GSharp.SourceLocation"]);
        Assert.StartsWith("DeviceErrorChannelKernels.cu.cs", ex.Data["GSharp.SourceLocation"]?.ToString());
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqMaxOnEmptySequenceOnCpuNative_CarriesSourceLocationAndMemberName()
    {
        int[] data = [1, 2, 3];
        int[] result = [0];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqMaxEmptyKernel(data, result, blockDimX: 1));

        Assert.Contains("Sequence contains no elements", ex.Message);
        Assert.Contains("in StackLinqMaxEmptyKernel at DeviceErrorChannelKernels.cu.cs", ex.Message);
        Assert.Contains("in thread (0,0,0) of block (0,0,0)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("StackLinqMaxEmptyKernel", ex.Data["GSharp.MemberName"]);
        Assert.NotNull(ex.Data["GSharp.SourceLocation"]);
        Assert.StartsWith("DeviceErrorChannelKernels.cu.cs", ex.Data["GSharp.SourceLocation"]?.ToString());
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void SwitchExpression_MatchedArmsOnCpuManagedAndNative_ReturnsArmValues()
    {
        int[] keys = [1, 0];
        int[] managed = new int[keys.Length];
        int[] native = new int[keys.Length];

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchKernel(keys, managed, blockDimX: 2);
        }

        DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchKernel(keys, native, blockDimX: 2);

        Assert.Equal([20, 10], managed);
        Assert.Equal([20, 10], native);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void SwitchExpression_UnmatchedArmOnCpuManagedAndNative_ThrowsWithSourceLocation()
    {
        int[] keys = [1, 2];
        int[] result = new int[keys.Length];

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            Assert.Throws<SwitchExpressionException>(() =>
                DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchKernel(keys, result, blockDimX: 2));
        }

        // The native build throws the exception C# defines, as the managed path does
        var ex = Assert.Throws<SwitchExpressionException>(() =>
            DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchKernel(keys, result, blockDimX: 2));

        Assert.Contains("Non-exhaustive switch expression failed to match its input.", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.SwitchExpression), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("SwitchExpressionNoMatchKernel", ex.Data["GSharp.MemberName"]);
        Assert.Equal("DeviceErrorChannelKernels.cu.cs(97,21)", ex.Data["GSharp.SourceLocation"]);
        Assert.Equal("(1,0,0)", ex.Data["GSharp.ThreadIndex"]);
    }

    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void SwitchExpression_UnmatchedArmInDeviceFunctionOnCpuNative_ThrowsWithSourcePlace()
    {
        int[] keys = [2];
        int[] result = [0];

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchInDeviceFunctionKernel(
                keys,
                result,
                blockDimX: 1
            )
        );

        Assert.Contains(
            "Non-exhaustive switch expression failed to match its input at DeviceErrorChannelKernels.cu.cs(108,44)",
            ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.SwitchExpression), ex.Data["GSharp.ErrorCode"]);
    }
#else
    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_CudaExecution_ThrowsStandardExceptionWithCoordinates()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [10, 20, 30];
        int[] result = [0];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceErrorChannelKernels.LaunchCoordinate3DTrapKernel(
                data,
                targetTx: 2,
                targetTy: 1,
                targetTz: 1,
                targetBx: 1,
                targetBy: 2,
                targetBz: 1,
                result,
                gridDimX: 3,
                gridDimY: 4,
                gridDimZ: 2,
                blockDimX: 4,
                blockDimY: 3,
                blockDimZ: 2));

        Assert.Contains("Index was out of range.", ex.Message);
        Assert.Contains("in thread (2,1,1) of block (1,2,1)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("(2,1,1)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(1,2,1)", ex.Data["GSharp.BlockIndex"]);
        Assert.Null(ex.InnerException);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_ConcurrentTrapOnCuda_FirstWriterWinsWithoutRace()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [1, 2, 3, 4];
        int[] result = new int[256];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchConcurrentTrapAllThreadsKernel(
                data, result, gridDimX: 4, blockDimX: 64));

        Assert.Contains("Sequence contains no matching element", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
        Assert.NotNull(ex.Data["GSharp.ThreadIndex"]);
        Assert.NotNull(ex.Data["GSharp.BlockIndex"]);
        Assert.Null(ex.InnerException);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_CudaExecution_CarriesSourceLocationAndMemberName()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [10, 20, 30];
        int[] result = [0];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqElementAtKernel(data, 999, result, blockDimX: 1));

        Assert.Contains("Index was out of range.", ex.Message);
        Assert.Contains("in StackLinqElementAtKernel at DeviceErrorChannelKernels.cu.cs", ex.Message);
        Assert.Contains("in thread (0,0,0) of block (0,0,0)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("StackLinqElementAtKernel", ex.Data["GSharp.MemberName"]);
        Assert.NotNull(ex.Data["GSharp.SourceLocation"]);
        Assert.StartsWith("DeviceErrorChannelKernels.cu.cs", ex.Data["GSharp.SourceLocation"]?.ToString());
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_GridOneHundredBlocksOnCuda_PreservesBlockCoordinate()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [10, 20, 30];
        int[] result = [0];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceErrorChannelKernels.LaunchBlockIndex100TrapKernel(data, targetBlock: 37, result, gridDimX: 100, blockDimX: 32));

        Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(37,0,0)", ex.Data["GSharp.BlockIndex"]);
        Assert.Contains("of block (37,0,0)", ex.Message);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqFirstOnEmptySequenceOnCuda_CarriesSourceLocationAndMemberName()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [1, 2, 3];
        int[] result = [0];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqFirstEmptyKernel(data, result, blockDimX: 1));

        Assert.Contains("Sequence contains no elements", ex.Message);
        Assert.Contains("in StackLinqFirstEmptyKernel at DeviceErrorChannelKernels.cu.cs", ex.Message);
        Assert.Contains("in thread (0,0,0) of block (0,0,0)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("StackLinqFirstEmptyKernel", ex.Data["GSharp.MemberName"]);
        Assert.NotNull(ex.Data["GSharp.SourceLocation"]);
        Assert.StartsWith("DeviceErrorChannelKernels.cu.cs", ex.Data["GSharp.SourceLocation"]?.ToString());
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
        Assert.Null(ex.InnerException);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqSingleOnMultipleElementsOnCuda_CarriesSourceLocationAndMemberName()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [10, 20];
        int[] result = [0];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqSingleMultipleKernel(data, result, blockDimX: 1));

        Assert.Contains("Sequence contains more than one element", ex.Message);
        Assert.Contains("in StackLinqSingleMultipleKernel at DeviceErrorChannelKernels.cu.cs", ex.Message);
        Assert.Contains("in thread (0,0,0) of block (0,0,0)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("StackLinqSingleMultipleKernel", ex.Data["GSharp.MemberName"]);
        Assert.NotNull(ex.Data["GSharp.SourceLocation"]);
        Assert.StartsWith("DeviceErrorChannelKernels.cu.cs", ex.Data["GSharp.SourceLocation"]?.ToString());
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
        Assert.Null(ex.InnerException);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Trap_StackLinqMaxOnEmptySequenceOnCuda_CarriesSourceLocationAndMemberName()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] data = [1, 2, 3];
        int[] result = [0];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeviceErrorChannelKernels.LaunchStackLinqMaxEmptyKernel(data, result, blockDimX: 1));

        Assert.Contains("Sequence contains no elements", ex.Message);
        Assert.Contains("in StackLinqMaxEmptyKernel at DeviceErrorChannelKernels.cu.cs", ex.Message);
        Assert.Contains("in thread (0,0,0) of block (0,0,0)", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("StackLinqMaxEmptyKernel", ex.Data["GSharp.MemberName"]);
        Assert.NotNull(ex.Data["GSharp.SourceLocation"]);
        Assert.StartsWith("DeviceErrorChannelKernels.cu.cs", ex.Data["GSharp.SourceLocation"]?.ToString());
        Assert.Equal("(0,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", ex.Data["GSharp.BlockIndex"]);
        Assert.Null(ex.InnerException);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void SwitchExpression_MatchedArmsOnCuda_ReturnsArmValues()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] keys = [1, 0];
        int[] result = new int[keys.Length];

        DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchKernel(keys, result, blockDimX: 2);

        Assert.Equal([20, 10], result);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void SwitchExpression_UnmatchedArmOnCuda_CarriesSourceLocation()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] keys = [1, 2];
        int[] result = new int[keys.Length];

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchKernel(keys, result, blockDimX: 2));

        Assert.Contains("Non-exhaustive switch expression failed to match its input.", ex.Message);
        Assert.Equal(nameof(GSharpErrorCode.SwitchExpression), ex.Data["GSharp.ErrorCode"]);
        Assert.Equal("SwitchExpressionNoMatchKernel", ex.Data["GSharp.MemberName"]);
        Assert.Equal("DeviceErrorChannelKernels.cu.cs(97,21)", ex.Data["GSharp.SourceLocation"]);
        Assert.Equal("(1,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Null(ex.InnerException);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void SwitchExpression_UnmatchedArmInDeviceFunctionOnCuda_ThrowsSwitchExpressionException()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        int[] keys = [2];
        int[] result = [0];

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            DeviceErrorChannelKernels.LaunchSwitchExpressionNoMatchInDeviceFunctionKernel(
                keys,
                result,
                blockDimX: 1
            )
        );

        Assert.Equal(nameof(GSharpErrorCode.SwitchExpression), ex.Data["GSharp.ErrorCode"]);
        Assert.Null(ex.InnerException);
    }
#endif
}
