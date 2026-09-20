namespace Kassyi.GSharp.Compiler.Helpers;

internal readonly struct EquatableDiagnostic(Diagnostic diagnostic) : IEquatable<EquatableDiagnostic>
{
    public Diagnostic Diagnostic { get; } = diagnostic;

    public bool Equals(EquatableDiagnostic other)
    {
        return Diagnostic.Id == other.Diagnostic.Id &&
               Diagnostic.Severity == other.Diagnostic.Severity &&
               Diagnostic.Location.Equals(other.Diagnostic.Location) &&
               Diagnostic.GetMessage() == other.Diagnostic.GetMessage();
    }

    public override bool Equals(object? obj) => obj is EquatableDiagnostic other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        hash = hash * 31 + HashHelper.ComputeFnv1AHash(Diagnostic.Id);
        hash = hash * 31 + Diagnostic.Severity.GetHashCode();
        hash = hash * 31 + (Diagnostic.Location?.GetHashCode() ?? 0);
        hash = hash * 31 + HashHelper.ComputeFnv1AHash(Diagnostic.GetMessage());
        return hash;
    }

    public static bool operator ==(EquatableDiagnostic left, EquatableDiagnostic right) => left.Equals(right);
    public static bool operator !=(EquatableDiagnostic left, EquatableDiagnostic right) => !left.Equals(right);
}

internal static class EquatableDiagnosticExtensions
{
    public static EquatableDiagnostic AsEquatable(this Diagnostic diagnostic) => new(diagnostic);
}
