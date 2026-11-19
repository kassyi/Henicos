namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The context of a <c>[GpuKernel]</c> or <c>[GpuDeviceFunction]</c> method together with the native stack switch
/// (<see cref="Models.GeneratorConfig.RunsOnNativeStacks"/>), as the translation steps take them.
/// </summary>
internal readonly struct NativeStackContext(GeneratorAttributeSyntaxContext syntax, bool onStacks)
{
    /// <summary>Whether the build also translates what can run on native stacks into its stack form.</summary>
    public bool OnStacks { get; } = onStacks;

    public SyntaxNode TargetNode => syntax.TargetNode;

    public ISymbol TargetSymbol => syntax.TargetSymbol;

    public SemanticModel SemanticModel => syntax.SemanticModel;
}
