using System.Diagnostics;
using System.Text;
using Kassyi.GSharp.Compiler.Tasks;
using Kassyi.GSharp.Compiler.Tasks.Helpers;
using Kassyi.GSharp.Tests.Shared.Tasks;
using Microsoft.Build.Utilities;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// Compiles the CUDA prelude with nvcc (no GPU launch) and checks the register spill of its failure path against a
/// fixed budget, so that a change to the prelude that makes it spill shows up as a test failure, not only as a GS0001
/// build warning of the modules that contain a recursive device function.
/// </summary>
/// <remarks>
/// [WHY] Sequential: the other tests of the compile tasks set and clear <c>GSHARP_TEST_NO_CUDA</c>, which would make
/// this compilation skip.
/// </remarks>
[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Compiler)]
public sealed class PreludeSpillBudgetTests
{
    // The spill stores ptxas reports for __gsharp_trap_site in a module compiled with the call ABI
    private const int TrapSiteSpillBudgetBytes = 0;

    private const string TrapSiteFunction = "_Z18__gsharp_trap_site15GSharpErrorCodej";

    static PreludeSpillBudgetTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    // [WHY] A recursive device function makes ptxas compile every function that is not inlined with the call ABI, which
    // is when __gsharp_trap_site saved its return address on the stack (8 bytes) for the __syncwarp it called
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void TrapSite_ModuleWithRecursiveFunction_StaysWithinSpillBudget()
    {
        Skip.If(Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA") == "1", "GSHARP_TEST_NO_CUDA=1 skips nvcc.");
        var directory = Path.Combine(Path.GetTempPath(), "gsharp-prelude-spill-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var engine = new MockBuildEngine();
            var runner = new RecordingProcessRunner();
            bool succeeded = CompileProbe(directory, engine, runner);

            Skip.If(engine.LoggedWarnings.Exists(static w => w.Contains("CUDA compiler (nvcc) was not found")),
                "nvcc is not available.");
            Assert.True(succeeded, string.Join(Environment.NewLine, engine.LoggedErrors));
            // [WHY] Read from the process output: ptxas writes its -v report to standard output on Windows, which the
            // task passes to the log only as a GS0001 warning
            string ptxasOutput = runner.Output;
            Assert.True(TryReadSpillStores(ptxasOutput, TrapSiteFunction, out int spillStores),
                $"ptxas reported no properties for {TrapSiteFunction}:{Environment.NewLine}{ptxasOutput}");
            Assert.True(spillStores <= TrapSiteSpillBudgetBytes,
                $"{TrapSiteFunction} spills {spillStores} bytes; the budget is {TrapSiteSpillBudgetBytes}.");
            Assert.DoesNotContain(engine.LoggedWarnings, static w => w.Contains(TrapSiteFunction));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool CompileProbe(string directory, MockBuildEngine engine, RecordingProcessRunner runner)
    {
        // Forward slashes: the host preprocessor reads backslashes in an #include path as escapes
        string preludePath = RepositoryPaths.Combine(
            "src", "Compiler", "Kassyi.GSharp.Compiler", "Resources", "Prelude.cu"
        );
        string prelude = string.Create(
            preludePath.Length,
            preludePath,
            static (span, path) =>
            {
                path.AsSpan().CopyTo(span);
                span.Replace('\\', '/');
            }
        );
        string source = Path.Combine(directory, "probe.cu");
        File.WriteAllText(source, $$"""
            #include "{{prelude}}"
            __device__ int __gsharp_fn_Depth(int n, int* a) {
            if (n < 0) { __gsharp_trap(GSharpErrorCode::IndexOutOfRange, 1u); }
            return n == 0 ? a[0] : __gsharp_fn_Depth(n - 1, a) + 1;
            }
            extern "C" __global__ void __gsharp_k_Probe(int* a, unsigned int __gsharp_epoch) {
            __gsharp_launch_epoch = __gsharp_epoch;
            a[threadIdx.x] = __gsharp_fn_Depth(a[threadIdx.x], a);
            }
            """);
        var task = new CompileCudaToFatbinTask
        {
            BuildEngine = engine,
            ProcessRunner = runner,
            CudaSources = [new TaskItem(source)],
            OutputDirectory = directory,
            CombinedFatbinName = "probe",
            TargetArchitectures = CompileCudaToFatbinTask.DefaultTargetArchitectures
        };
        return task.Execute();
    }

    // Reads "<n> bytes spill stores" from the line after "Function properties for <function>"
    private static bool TryReadSpillStores(string ptxasOutput, string function, out int spillStores)
    {
        const string SpillStores = " bytes spill stores";
        spillStores = 0;
        string header = "Function properties for " + function;
        bool headerSeen = false;
        foreach (ReadOnlySpan<char> line in ptxasOutput.AsSpan().EnumerateLines())
        {
            if (!headerSeen)
            {
                headerSeen = line.TrimEnd().EndsWith(header, StringComparison.Ordinal);
                continue;
            }

            // "<n> bytes stack frame, <n> bytes spill stores, <n> bytes spill loads"
            int end = line.IndexOf(SpillStores, StringComparison.Ordinal);
            if (end < 0)
            {
                return false;
            }

            ReadOnlySpan<char> beforeEnd = line[..end];
            ReadOnlySpan<char> number = beforeEnd[(beforeEnd.LastIndexOf(' ') + 1)..];
            return int.TryParse(number, out spillStores);
        }

        return false;
    }

    // Runs the processes of the task and keeps their standard output and error
    private sealed class RecordingProcessRunner : IProcessRunner
    {
        private readonly StringBuilder _output = new();

        public string Output => _output.ToString();

        public ProcessResult Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default) =>
            Record(ProcessRunner.Default.Run(startInfo, cancellationToken));

        public async Task<ProcessResult> RunAsync(
            ProcessStartInfo startInfo,
            CancellationToken cancellationToken = default) =>
            Record(await ProcessRunner.Default.RunAsync(startInfo, cancellationToken));

        private ProcessResult Record(ProcessResult result)
        {
            lock (_output)
            {
                _output.AppendLine(result.StandardOutput).AppendLine(result.StandardError);
            }

            return result;
        }
    }
}
