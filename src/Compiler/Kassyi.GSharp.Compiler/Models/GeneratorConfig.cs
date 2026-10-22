namespace Kassyi.GSharp.Compiler.Models;

internal record struct GeneratorConfig(
    string TargetDialect,
    bool EmitSource,
    string? IntermediateOutputPath)
{
    /// <summary>Whether <c>GSharpTargetDialect</c> selects the CUDA dialect; any other value selects the C++ dialect.</summary>
    public readonly bool IsCuda => TargetDialect.Equals("Cuda", StringComparison.OrdinalIgnoreCase);

    /// <summary>The generated class that holds the prelude source shared by every kernel.</summary>
    public readonly string SharedPreludeClassName => $"GSharpShared{TargetDialect}Prelude";

    /// <summary>The generated class that holds the device function source shared by every kernel.</summary>
    public readonly string SharedDeviceCodeClassName => $"GSharpShared{TargetDialect}DeviceCode";

    public readonly ITranspilerDialect CreateDialect() => IsCuda ? new GSharpCudaDialect() : new StandardCppDialect();
}
