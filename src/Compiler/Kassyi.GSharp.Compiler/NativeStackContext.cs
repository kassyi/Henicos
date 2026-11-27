namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The context of a <c>[GpuKernel]</c> or <c>[GpuDeviceFunction]</c> method together with the native stack switch
/// (<see cref="Models.GeneratorConfig.RunsOnNativeStacks"/>) and whether the assembly exports its device code
/// (<see cref="Models.GeneratorConfig.ExportDeviceCode"/>), as the translation steps take them.
/// </summary>
internal readonly struct NativeStackContext(
    GeneratorAttributeSyntaxContext syntax,
    bool onStacks,
    bool exportsDeviceCode = false)
{
    /// <summary>Whether the build also translates what can run on native stacks into its stack form.</summary>
    public bool OnStacks { get; } = onStacks;

    /// <summary>
    /// Whether the assembly exports its device code records (<see cref="Models.GeneratorConfig.ExportDeviceCode"/>),
    /// so suspending device functions also translate their native stack form for the record.
    /// </summary>
    public bool ExportsDeviceCode { get; } = exportsDeviceCode;

    public SyntaxNode TargetNode => syntax.TargetNode;

    public ISymbol TargetSymbol => syntax.TargetSymbol;

    public SemanticModel SemanticModel => syntax.SemanticModel;
}
