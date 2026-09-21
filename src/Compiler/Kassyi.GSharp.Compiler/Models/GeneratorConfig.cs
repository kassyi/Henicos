namespace Kassyi.GSharp.Compiler.Models;

internal record struct GeneratorConfig(
    string TargetDialect,
    bool EmitSource,
    string? IntermediateOutputPath);
