using System.IO;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class NativeLibraryProbeTests
{
    private static readonly string s_baseDirectory = Path.Combine(Path.GetTempPath(), "gsharp-probe-base");

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ResolveCandidatePath_BareName_IsLeftForOsSearch()
    {
        Assert.Equal("cublas64_13.dll", NativeLibraryProbe.ResolveCandidatePath("cublas64_13.dll", s_baseDirectory));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("./native/kernels.dll", "native/kernels.dll")]
    [InlineData("native/kernels.dll", "native/kernels.dll")]
    [InlineData(".\\native\\kernels.dll", "native/kernels.dll")]
    public void Test_ResolveCandidatePath_RelativePath_IsAnchoredToBaseDirectory(string candidate, string expectedRelative)
    {
        var resolved = NativeLibraryProbe.ResolveCandidatePath(candidate, s_baseDirectory);

        Assert.Equal(Path.GetFullPath(Path.Combine(s_baseDirectory, expectedRelative)), resolved);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ResolveCandidatePath_RootedPath_IsUnchanged()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "abs", "lib.dll");

        Assert.Equal(rooted, NativeLibraryProbe.ResolveCandidatePath(rooted, s_baseDirectory));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ResolveCandidatePath_UndefinedEnvironmentVariable_IsLeftUnexpanded()
    {
        const string Candidate = "%GSHARP_PROBE_SURELY_UNDEFINED_VAR%/lib.dll";

        var resolved = NativeLibraryProbe.ResolveCandidatePath(Candidate, s_baseDirectory);

        Assert.Contains("%GSHARP_PROBE_SURELY_UNDEFINED_VAR%", resolved);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Load_AllCandidatesMissing_ReturnsZeroWithoutThrowing()
    {
        string[] candidates = ["gsharp_missing_lib_a", "./gsharp/missing_lib_b.dll"];

        var handle = NativeLibraryProbe.Load(typeof(NativeLibraryProbeTests).Assembly, null, candidates);

        Assert.Equal(IntPtr.Zero, handle);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Load_FallsThroughMissingCandidates_ToFirstLoadable()
    {
        var loadable = OperatingSystem.IsWindows() ? "kernel32.dll" : "libc.so.6";
        string[] candidates = ["gsharp_missing_lib_a", loadable];

        var handle = NativeLibraryProbe.Load(typeof(NativeLibraryProbeTests).Assembly, null, candidates);

        Assert.NotEqual(IntPtr.Zero, handle);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Load_EmptyCandidates_ReturnsZero()
    {
        var handle = NativeLibraryProbe.Load(typeof(NativeLibraryProbeTests).Assembly, null, []);

        Assert.Equal(IntPtr.Zero, handle);
    }
}

[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class NativeLibraryProbeEnvironmentTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ResolveCandidatePath_ExpandsEnvironmentVariables()
    {
        const string VariableName = "GSHARP_PROBE_TEST_HOME";
        var home = Path.Combine(Path.GetTempPath(), "gsharp-probe-home");
        Environment.SetEnvironmentVariable(VariableName, home);
        try
        {
            var resolved = NativeLibraryProbe.ResolveCandidatePath($"%{VariableName}%/bin/lib.dll", "unused");

            Assert.Equal(Path.GetFullPath(Path.Combine(home, "bin", "lib.dll")), resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VariableName, null);
        }
    }
}
