using System.Text.Json.Serialization;

/// <summary>The collected report; the shape of the <c>--json</c> output.</summary>
public sealed class ReportModel
{
    public string Os { get; set; } = Collector.NotFound;
    public string Cpu { get; set; } = Collector.NotFound;
    public string Memory { get; set; } = Collector.NotFound;
    public string Motherboard { get; set; } = Collector.NotFound;
    public List<string> Gpus { get; set; } = [];
    public string CudaToolkit { get; set; } = Collector.NotFound;
    public string CudaPath { get; set; } = "unset";
    public string HostCpp { get; set; } = Collector.NotFound;
    public string DotnetSdk { get; set; } = Collector.NotFound;
    public string GlobalJson { get; set; } = "none";
    public List<string> DotnetSdks { get; set; } = [];
    public List<string> DotnetRuntimes { get; set; } = [];
    public string? GSharp { get; set; }
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReportModel))]
internal sealed partial class ReportJsonContext : JsonSerializerContext;
