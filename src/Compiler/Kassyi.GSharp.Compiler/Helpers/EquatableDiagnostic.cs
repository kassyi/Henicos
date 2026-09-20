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

    public override int GetHashCode() => HashHelper.Combine(Diagnostic.Id, Diagnostic.Severity, Diagnostic.Location,
        Diagnostic.GetMessage());

    public static bool operator ==(EquatableDiagnostic left, EquatableDiagnostic right) => left.Equals(right);
    public static bool operator !=(EquatableDiagnostic left, EquatableDiagnostic right) => !left.Equals(right);

    public override string ToString() => Diagnostic.ToString();
}

internal static class EquatableDiagnosticExtensions
{
    public static EquatableDiagnostic AsEquatable(this Diagnostic diagnostic) => new(diagnostic);
}
