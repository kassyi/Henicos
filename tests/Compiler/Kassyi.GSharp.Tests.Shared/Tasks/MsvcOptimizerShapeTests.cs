#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Compiler.Tasks.Helpers;
using Kassyi.GSharp.Compiler.Tasks.Toolchains;
using Microsoft.Build.Utilities;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

/// <summary>
/// Builds the integer shapes that MSVC's global optimizer (<c>/Og</c>, part of <c>/O2</c>) miscompiles (#1078) as
/// standalone C++ (<c>TestCases/Tasks/MsvcOptimizerShapes.cpp</c>) with the flags of <see cref="MsvcToolchain"/>, and
/// checks the values at run time. The C++ file proves each expected value with a <c>static_assert</c>, so a different
/// value is a miscompile. This guards the flags themselves, apart from what G# emits and how MSVC inlines it into a
/// launcher (the differential cases of <c>NativeOptimizerDifferentialTests</c> depend on both).
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
[Scenario(TestScenario.Boundary)]
public sealed class MsvcOptimizerShapeTests
{
    static MsvcOptimizerShapeTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [SkippableFact]
    public unsafe void MsvcToolchain_OptimizerShapes_KeepTheirCppValues()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "MSVC builds the native module on Windows only.");
        Skip.IfNot(RuntimeInformation.ProcessArchitecture == Architecture.X64, "The shapes are checked on x64.");

        var engine = new MockBuildEngine();
        var log = new TaskLoggingHelper(engine, nameof(MsvcOptimizerShapeTests));
        var toolchain = new MsvcToolchain(ProcessRunner.Default);
        Skip.If(
            toolchain.PrepareEnvironment(["x64"], log) != ToolchainPreparationResult.Ready,
            "MSVC (vcvarsall.bat) is not available.");

        var directory = Path.Combine(Path.GetTempPath(), "gsharp-msvc-shapes-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "shapes.cpp");
            File.WriteAllText(source, RoslynTestUtility.ReadTestCase("Tasks/MsvcOptimizerShapes.cpp"));
            var library = Path.Combine(directory, "shapes.dll");

            Assert.True(
                toolchain.TryCompileCppToSharedLibrary(source, library, "x64", log),
                string.Join(Environment.NewLine, engine.LoggedErrors));

            var handle = NativeLibrary.Load(library);
            try
            {
                var negateThenDivide = (delegate* unmanaged<int, long>)NativeLibrary.GetExport(
                    handle,
                    "GSharpNegateThenDivide");
                var shortWidenedTwice = (delegate* unmanaged<int*, int, ulong>)NativeLibrary.GetExport(
                    handle,
                    "GSharpShortWidenedTwice");
                var shortIncrementPicksPointer = (delegate* unmanaged<int, int>)NativeLibrary.GetExport(
                    handle,
                    "GSharpShortIncrementPicksPointer");
                var sbyteSubtractPicksPointer = (delegate* unmanaged<int, int>)NativeLibrary.GetExport(
                    handle,
                    "GSharpSByteSubtractPicksPointer");

                // The expected values are those of the static_asserts in the C++ file
                Assert.Equal(-(long)0x4000_0000, negateThenDivide(int.MinValue));
                int* minusOne = stackalloc int[] { 0, -1 };
                Assert.Equal(0x0835_6830_B4EB_6AD4UL, shortWidenedTwice(minusOne, 1));
                Assert.Equal(short.MinValue, shortIncrementPicksPointer(short.MaxValue));
                Assert.Equal(20, sbyteSubtractPicksPointer(-2));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
