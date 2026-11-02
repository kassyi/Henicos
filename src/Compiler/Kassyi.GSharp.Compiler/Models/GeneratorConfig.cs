namespace Kassyi.GSharp.Compiler.Models;

/// <param name="TargetDialect">The <c>GSharpTargetDialect</c> of the project.</param>
/// <param name="EmitSource">Whether <c>GSharpEmitSource</c> embeds the generated C++ as constants.</param>
/// <param name="IntermediateOutputPath">Where the AOT artifacts are written, or <see langword="null"/>.</param>
/// <param name="ExportDeviceCode">
/// Whether the assembly records its device code for other assemblies (<c>GSharpExportDeviceCode</c>, default true).
/// </param>
/// <param name="IsCheckedBuild">
/// Whether <c>GSharpCheckedBuild</c> enables checked device assertions and post-launch stream synchronization (#729).
/// </param>
internal record struct GeneratorConfig(
    string TargetDialect,
    bool EmitSource,
    string? IntermediateOutputPath,
    bool ExportDeviceCode = true,
    bool IsCheckedBuild = false)
{
    /// <summary>Whether <c>GSharpTargetDialect</c> selects the CUDA dialect; any other value selects the C++ dialect.</summary>
    public readonly bool IsCuda => TranspilerDialects.IsCuda(TargetDialect);

    /// <summary>The generated class that holds the prelude source shared by every kernel.</summary>
    public readonly string SharedPreludeClassName => $"GSharpShared{TargetDialect}Prelude";

    /// <summary>The generated class that holds the device function source shared by every kernel.</summary>
    public readonly string SharedDeviceCodeClassName => $"GSharpShared{TargetDialect}DeviceCode";

    public readonly ITranspilerDialect CreateDialect() => TranspilerDialects.Create(TargetDialect);
}
