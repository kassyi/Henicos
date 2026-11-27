namespace Kassyi.GSharp.Compiler.Analysis;

internal readonly struct SiteMaskKey(SyntaxNode syntax, IMethodSymbol target) : IEquatable<SiteMaskKey>
{
    public SyntaxNode Syntax { get; } = syntax;

    public IMethodSymbol Target { get; } = target;

    public bool Equals(SiteMaskKey other) => Syntax == other.Syntax && Target.IsEqualTo(other.Target);

    public override bool Equals(object? obj) => obj is SiteMaskKey other && Equals(other);

    public override int GetHashCode() =>
        unchecked((Syntax.GetHashCode() * 397) ^ SymbolEqualityComparer.Default.GetHashCode(Target));
}
