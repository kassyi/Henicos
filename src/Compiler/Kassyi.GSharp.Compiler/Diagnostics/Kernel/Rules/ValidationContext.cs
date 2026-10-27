namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>Encapsulates semantic and method symbol context for AST validation rules.</summary>
/// <param name="semanticModel">The semantic model of the method.</param>
/// <param name="methodSymbol">The kernel or device function that is checked.</param>
/// <param name="reportDiagnostic">Receives the diagnostics of the rules.</param>
/// <param name="dialect">
/// The dialect of the project (<c>GSharpTargetDialect</c>), for the checks that depend on its capabilities; the C++
/// dialect when not given, as for a project that does not set the property.
/// </param>
/// <param name="exportsDeviceCode">Whether the project exports its device code (<c>GSharpExportDeviceCode</c>).</param>
internal readonly struct ValidationContext(
    SemanticModel semanticModel,
    IMethodSymbol methodSymbol,
    Action<Diagnostic> reportDiagnostic,
    ITranspilerDialect? dialect = null,
    bool exportsDeviceCode = true)
{
    public readonly SemanticModel SemanticModel = semanticModel;
    public readonly IMethodSymbol MethodSymbol = methodSymbol;
    public readonly ITranspilerDialect Dialect = dialect ?? TranspilerDialects.Create(TranspilerDialects.DefaultName);

    /// <summary>Whether the project exports its device code (<c>GSharpExportDeviceCode</c>, the default).</summary>
    public readonly bool ExportsDeviceCode = exportsDeviceCode;

    /// <summary>
    /// Whether the method being checked has a managed form that calls the managed state machines of the suspending
    /// functions it calls: a GPU function on the C++ dialect (the managed fallback), or a device function of a project
    /// that exports its device code (whose state machine is emitted on every dialect).
    /// </summary>
    public bool NeedsManagedStateMachines =>
        !Dialect.IsCuda || (ExportsDeviceCode && !MethodSymbol.HasAttribute(KnownAttributes.GpuKernel));

    public void ReportDiagnostic(Diagnostic diagnostic) => reportDiagnostic(diagnostic);

    public void ReportDiagnostic(DiagnosticDescriptor descriptor, Location? location, params object?[]? messageArgs) =>
        reportDiagnostic.ReportDiagnostic(descriptor, location, messageArgs);

    public void ReportDiagnostic(DiagnosticDescriptor descriptor, SyntaxNode node, params object?[]? messageArgs) =>
        reportDiagnostic.ReportDiagnostic(descriptor, node, messageArgs);

}
