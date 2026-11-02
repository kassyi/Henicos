using System.Collections.Immutable;
using System.Diagnostics;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Naming;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Verifies the device runtime error channel and CPU/GPU exception parity (#729).
/// </summary>
public sealed class DeviceErrorChannelTests
{
    private const string TrapTestVariable = "GSHARP_TEST_CUDA_TRAP";

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
            { GSharpErrorCode.DataRace, typeof(InvalidOperationException) }
        };

    [Theory]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(AllErrorCodesAndExceptionTypes))]
    public void Test_T729_1_ErrorMapper_MapsAllErrorCodesToStandardExceptionsWithMetadata(
        GSharpErrorCode code,
        Type expectedExceptionType)
    {
        var moduleId = GSharpErrorMapper.RegisterErrorSites(
            $"test-module-{code}",
            [
                new GSharpErrorSite("MyKernels.cu.cs", 42, 13, "MyKernel", $"Custom error for {code}")
            ]);

        var compositeSiteId = (moduleId << 20) | 1u;
        var packedThread = GSharpErrorMapper.PackThreadIndex(7, 3, 1);
        var gridDim = (X: 5, Y: 6, Z: 2);
        var blockLinear = (uint)((1 * gridDim.Y + 4) * gridDim.X + 2);
        var innerCuda = new CudaException(CUresult.CudaErrorIllegalInstruction);

        var slot = new GSharpDeviceErrorSlot
        {
            Code = (uint)code | GSharpErrorMapper.CompletionBit | (5u << 20) | (6u << 26),
            SiteId = compositeSiteId,
            ThreadLinear = packedThread,
            BlockLinear = blockLinear
        };

        var ex = GSharpErrorMapper.CreateExceptionFromSlot(in slot, (1, 1, 1), innerCuda);

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
    public void Test_T729_5_CoordinatePackingAndUnpacking_3D()
    {
        var packedThread = GSharpErrorMapper.PackThreadIndex(511, 15, 3);
        Assert.Equal((511, 15, 3), GSharpErrorMapper.UnpackThreadIndex(packedThread));

        var gridDim = (X: 7, Y: 11, Z: 5);
        var blockLinear = (uint)((3 * gridDim.Y + 9) * gridDim.X + 4);
        Assert.Equal((4, 9, 3), GSharpErrorMapper.UnpackBlockIndex(blockLinear, gridDim));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_T729_7_ErrorSiteTable_RegistersDeduplicatesAndRoundTripsSymbolFile()
    {
        var table = new ErrorSiteTable();
        var id1 = table.Register("Sample.cu.cs", 10, 5, "SampleKernel", "Attempted to divide by zero.");
        var id2 = table.Register("Sample.cu.cs", 20, 9, "HelperMethod", "Index was outside the bounds of the array.");
        var id1Duplicate = table.Register("Sample.cu.cs", 10, 5, "SampleKernel", "Attempted to divide by zero.");

        Assert.Equal(1u, id1);
        Assert.Equal(2u, id2);
        Assert.Equal(1u, id1Duplicate);
        Assert.Equal(2, table.Count);

        using var writer = new SourceWriter();
        ErrorSiteTable.AppendTrapSiteCall(writer, "DivideByZero", id1);
        Assert.Equal("__gsharp_trap_site(GSharpErrorCode::DivideByZero, 1u)", writer.ToString());

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
        Assert.Equal(new GSharpNameEncoding.SymbolErrorSiteRecord(1u, "Sample.cu.cs", 10, 5, "SampleKernel", "Attempted to divide by zero."), parsed[1u]);
        Assert.Equal(new GSharpNameEncoding.SymbolErrorSiteRecord(2u, "Sample.cu.cs", 20, 9, "HelperMethod", "Index was outside the bounds of the array."), parsed[2u]);
    }

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_T729_6_CheckedBuildOption_ControlsPostLaunchStreamSynchronization(bool isCheckedBuild)
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

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(GpuKernelAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(CudaDriverApi).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            "SampleAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

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

        if (isCheckedBuild)
        {
            Assert.Contains("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));", wrapperText);
        }
        else
        {
            Assert.DoesNotContain("cuStreamSynchronize", wrapperText);
        }
    }

#if !GSHARP_CUDA
    [Fact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_T729_2_CpuManagedAndNative_StackLinqTrapsThrowStandardExceptions()
    {
        int[] data = [1, 2, 3, 4];
        int[] result = [0];

        // 1. Managed execution
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                DeviceErrorChannelKernels.LaunchStackLinqFirstNoMatchKernel(data, 100, result, blockDimX: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DeviceErrorChannelKernels.LaunchStackLinqElementAtKernel(data, 99, result, blockDimX: 1));
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = false;
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
    public void Test_T729_4_CpuNative_ConcurrentTrapAllThreads_FirstWriterWins()
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
    public void Test_T729_5_CpuNative_3DCoordinates_CapturedInExceptionData()
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
#else
    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_T729_2_Cuda_SubprocessTrap_ThrowsStandardExceptionWithCoordinates()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        if (Environment.GetEnvironmentVariable(TrapTestVariable) == "1")
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

            Assert.Contains("Index was out of range.", ex.Message);
            Assert.Contains("in thread (2,1,1) of block (1,2,1)", ex.Message);
            Assert.Equal(nameof(GSharpErrorCode.ArgumentOutOfRange), ex.Data["GSharp.ErrorCode"]);
            Assert.Equal("(2,1,1)", ex.Data["GSharp.ThreadIndex"]);
            Assert.Equal("(1,2,1)", ex.Data["GSharp.BlockIndex"]);
            var cudaInner = Assert.IsType<CudaException>(ex.InnerException);
            Assert.True(cudaInner.Result is CUresult.CudaErrorIllegalInstruction or CUresult.CudaErrorLaunchFailed);
            return;
        }

        RunTrapTestInIsolatedProcess(nameof(Test_T729_2_Cuda_SubprocessTrap_ThrowsStandardExceptionWithCoordinates));
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_T729_4_Cuda_SubprocessConcurrentTrap_FirstWriterWinsWithoutRace()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        if (Environment.GetEnvironmentVariable(TrapTestVariable) == "1")
        {
            int[] data = [1, 2, 3, 4];
            int[] result = new int[256];

            var ex = Assert.Throws<InvalidOperationException>(() =>
                DeviceErrorChannelKernels.LaunchConcurrentTrapAllThreadsKernel(
                    data, result, gridDimX: 4, blockDimX: 64));

            Assert.Contains("Sequence contains no matching element", ex.Message);
            Assert.Equal(nameof(GSharpErrorCode.InvalidOperation), ex.Data["GSharp.ErrorCode"]);
            Assert.NotNull(ex.Data["GSharp.ThreadIndex"]);
            Assert.NotNull(ex.Data["GSharp.BlockIndex"]);
            var cudaInner = Assert.IsType<CudaException>(ex.InnerException);
            Assert.True(cudaInner.Result is CUresult.CudaErrorIllegalInstruction or CUresult.CudaErrorLaunchFailed);
            return;
        }

        RunTrapTestInIsolatedProcess(nameof(Test_T729_4_Cuda_SubprocessConcurrentTrap_FirstWriterWinsWithoutRace));
    }

    private static void RunTrapTestInIsolatedProcess(string testMethodName)
    {
        var testAssemblyPath = typeof(DeviceErrorChannelTests).Assembly.Location;
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("vstest");
        psi.ArgumentList.Add(testAssemblyPath);
        psi.ArgumentList.Add($"--Tests:Kassyi.GSharp.Tests.Shared.Runtime.DeviceErrorChannelTests.{testMethodName}");
        psi.Environment[TrapTestVariable] = "1";
        psi.Environment["DiffEngine_Disabled"] = "true";
        psi.Environment["CI"] = "true";

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet vstest subprocess.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        Assert.True(proc.WaitForExit(60_000), $"Subprocess timed out.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        Assert.True(proc.ExitCode == 0, $"Isolated CUDA trap test '{testMethodName}' failed with exit code {proc.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }
#endif
}
