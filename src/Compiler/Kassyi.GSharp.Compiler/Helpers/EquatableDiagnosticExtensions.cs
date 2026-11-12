namespace Kassyi.GSharp.Compiler.Helpers;

internal static class EquatableDiagnosticExtensions
{
    public static EquatableDiagnostic AsEquatable(this Diagnostic diagnostic) => new(diagnostic);
}
