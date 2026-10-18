namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>Provides helper extension methods for zero-ceremony diagnostic reporting.</summary>
internal static class DiagnosticExtensions
{
    /// <summary>
    /// Reports a diagnostic using the provided callback delegate if non-null.
    /// </summary>
    public static void ReportDiagnostic(this Action<Diagnostic>? reportDiagnostic, DiagnosticDescriptor descriptor,
        Location? location, params object?[]? messageArgs) =>
        reportDiagnostic?.Invoke(Diagnostic.Create(descriptor, location, messageArgs));

    /// <summary>
    /// Reports a diagnostic at the syntax node's location using the provided callback delegate if non-null.
    /// </summary>
    public static void ReportDiagnostic(this Action<Diagnostic>? reportDiagnostic, DiagnosticDescriptor descriptor,
        SyntaxNode node, params object?[]? messageArgs) =>
        reportDiagnostic?.Invoke(Diagnostic.Create(descriptor, node.GetLocation(), messageArgs));

    /// <summary>
    /// Reports a diagnostic for the given syntax node analysis context.
    /// </summary>
    public static void ReportDiagnostic(this SyntaxNodeAnalysisContext context, DiagnosticDescriptor descriptor,
        Location? location, params object?[]? messageArgs) =>
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, messageArgs));

    /// <summary>
    /// Reports a diagnostic at the syntax node's location for the given syntax node analysis context.
    /// </summary>
    public static void ReportDiagnostic(this SyntaxNodeAnalysisContext context, DiagnosticDescriptor descriptor, SyntaxNode node,
        params object?[]? messageArgs) =>
        context.ReportDiagnostic(Diagnostic.Create(descriptor, node.GetLocation(), messageArgs));

    /// <summary>
    /// Reports a diagnostic for the given symbol analysis context.
    /// </summary>
    public static void ReportDiagnostic(this SymbolAnalysisContext context, DiagnosticDescriptor descriptor, Location? location,
        params object?[]? messageArgs) =>
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, messageArgs));
}
