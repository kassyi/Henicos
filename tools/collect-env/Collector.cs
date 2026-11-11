using static Privacy;
using static ProcessProbe;

/// <summary>Entry point of the report: the list of probe calls that fill <see cref="ReportModel"/>.</summary>
internal static class Collector
{
    public const string NotFound = "not found";

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
