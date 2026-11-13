namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>The closed copy of one instance.</summary>
/// <param name="Instance">The instance.</param>
/// <param name="Declaration">The copy in the derived compilation, or <see langword="null"/> when none could be written.</param>
/// <param name="Symbol">The method the copy declares.</param>
/// <param name="Findings">
/// Where the copy does not mean what the declaration means, at the node of the declaration; a copy with a finding is
/// not emitted.
/// </param>
internal sealed record GenericCopy(
    GenericInstance Instance,
    MethodDeclarationSyntax? Declaration,
    IMethodSymbol? Symbol,
    ImmutableArray<CopyFinding> Findings)
{
    /// <summary>Whether the copy means what the declaration means and can be emitted.</summary>
    public bool IsFaithful => Declaration is not null && Findings.IsEmpty;
}
