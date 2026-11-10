namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>Where an IR node comes from in the user's source: a span of a tree of the compilation.</summary>
/// <param name="TreeOrdinal">The index of the tree in <see cref="Compilation.SyntaxTrees"/>.</param>
/// <param name="Start">The start of the span (without trivia).</param>
/// <param name="Length">The length of the span.</param>
/// <param name="Region">
/// The copy of an inlined body the node belongs to; 0 outside any copy. Variables declared in different copies are
/// different variables.
/// </param>
/// <param name="Synthesized">
/// Whether the node stands for no syntax of its own: an implicit operation (an implicit conversion, a default argument)
/// or a node that a pass created. Its span is still that of the syntax it was made for.
/// </param>
internal readonly record struct IrSource(int TreeOrdinal, int Start, int Length, int Region, bool Synthesized)
{
    /// <summary>The span in the tree.</summary>
    public TextSpan Span => new(Start, Length);

    /// <summary>The same position, marked as made by the compiler.</summary>
    public IrSource AsSynthesized() => this with { Synthesized = true };

    /// <summary>The location in <paramref name="compilation"/>, for a diagnostic.</summary>
    public Location ToLocation(Compilation compilation) =>
        Location.Create(SemanticModelExtensions.GetSyntaxTrees(compilation)[TreeOrdinal], Span);
}
