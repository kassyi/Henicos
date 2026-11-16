namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports a warning (GSH0116) when a kernel enables <c>[GpuKernel(AllowOversizedSharedMemory = true)]</c> but no
/// GPU of <c>GSharpTargetArchitectures</c> has an oversized shared memory configuration.
/// </summary>
internal sealed class OversizedSharedMemoryTargetRule : ISyntaxValidationRule<MethodDeclarationSyntax>
{
    public static readonly OversizedSharedMemoryTargetRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.OversizedSharedMemoryHasNoTarget);

    private OversizedSharedMemoryTargetRule()
    {
    }

    public void Analyze(MethodDeclarationSyntax node, ref ValidationContext context)
    {
        var method = context.MethodSymbol;
        if (!method.AllowsOversizedSharedMemory() || !context.TargetLimits.HasNoOversizedTarget)
        {
            return;
        }

        if (FindAllowOversizedArgument(method, node) is not { } argument)
        {
            return;
        }

        var architectures = string.Join(", ",
            context.TargetLimits.Architectures
                .AsStackEnumerable()
                .Select(static a => a.Name)
                .ToArray());
        context.ReportDiagnostic(
            GSharpDiagnostics.OversizedSharedMemoryHasNoTarget,
            argument,
            KernelHostNames.GetMessageName(method),
            architectures);
    }

    // The AllowOversizedSharedMemory argument of the method's [GpuKernel] attribute, when this declaration holds the
    // attribute (another part of a partial method reports it there)
    private static AttributeArgumentSyntax? FindAllowOversizedArgument(IMethodSymbol method,
        MethodDeclarationSyntax node)
    {
        if (method.GetAttribute(KnownAttributes.GpuKernel)?.ApplicationSyntaxReference?.GetSyntax() is not
                AttributeSyntax { ArgumentList: { } arguments } attribute ||
            attribute.SyntaxTree != node.SyntaxTree ||
            !node.Span.Contains(attribute.Span))
        {
            return null;
        }

        foreach (var argument in arguments.Arguments)
        {
            if (argument.NameEquals?.Name.Identifier.ValueText == "AllowOversizedSharedMemory")
            {
                return argument;
            }
        }

        return null;
    }
}
