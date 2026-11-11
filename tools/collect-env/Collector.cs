using static Privacy;
using static ProcessProbe;

/// <summary>Entry point of the report: the list of probe calls that fill <see cref="ReportModel"/>.</summary>
internal static class Collector
{
    public const string NotFound = "not found";

    public const string HelpText =
        """
        collect-env: prints the environment of a G# bug report (OS, CPU, GPU, CUDA, host C++ compiler, .NET, checkout).

        Usage: collect-env [--output <file>] [--json] [--help]

          --output <file>   write the report to <file> instead of stdout
          --json            machine-readable JSON instead of Markdown
          --help            show this text

        The report leaves out the user name, host name, serial numbers and paths under the user profile (shown as ~).
        CUDA_PATH is reported as set or unset only. A missing tool is shown as "not found".

        """;

    public static ReportModel Collect()
    {
        var model = new ReportModel();
        bool windows = OperatingSystem.IsWindows();
        model.Os = windows ? HardwareProbe.WindowsOs() : HardwareProbe.LinuxOs();
        string cpu = windows ? HardwareProbe.WindowsCpu() : HardwareProbe.LinuxCpu();
        model.Cpu = Clean($"{cpu}, {Environment.ProcessorCount} logical cores");
        model.Memory = HardwareProbe.Memory(windows);
        model.Motherboard = windows ? HardwareProbe.WindowsBoard() : HardwareProbe.LinuxBoard();
        GpuProbe.CollectGpus(model);
        model.CudaToolkit = GpuProbe.CudaToolkit();
        model.CudaPath = IsSet("CUDA_PATH") ? "set" : "unset";
        model.HostCpp = windows ? ToolchainProbe.Msvc() : ToolchainProbe.LinuxCompilers();
        ToolchainProbe.CollectDotnet(model);
        model.GSharp = CheckoutProbe.GSharpCheckout();
        CheckoutProbe.CollectEnvironmentVariables(model);
        return model;
    }
}
