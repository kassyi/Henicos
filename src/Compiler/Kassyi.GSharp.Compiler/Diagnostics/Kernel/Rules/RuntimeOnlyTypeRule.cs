namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports each reference to a type that only the .NET runtime can give meaning to: <c>dynamic</c>,
/// <see cref="TypedReference"/> and <c>GCHandle</c> (GSH0003, <see cref="GpuCodeConstraints.GetRuntimeOnlyTypeName"/>).
/// </summary>
/// <remarks>
/// The type is decided by the symbol that the name binds to, so <c>var</c> reports the type it stands for and a user
/// type with the same name is not reported. <c>__makeref</c> needs no rule: the transpiler has no rule for it, so it is
/// reported as an unsupported syntax kind.
/// </remarks>
internal sealed class RuntimeOnlyTypeRule : ISyntaxValidationRule<IdentifierNameSyntax>
{
    public static readonly RuntimeOnlyTypeRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(GSharpDiagnostics.UnsupportedSyntax);

    private RuntimeOnlyTypeRule()
    {
    }

    public void Analyze(IdentifierNameSyntax node, ref ValidationContext context)
    {
        if (GpuCodeConstraints.GetRuntimeOnlyTypeName(context.SemanticModel.GetSymbol(node) as ITypeSymbol) is { } name)
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node, name);
        }
    }
}
