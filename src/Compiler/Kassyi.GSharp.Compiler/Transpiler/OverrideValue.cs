namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Represents a replacement substitution value (literal string snippet or Roslyn syntax node) used during macro expansion.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Blittable readonly value type to ensure zero-allocation storage in dictionaries.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public readonly struct OverrideValue
{
    /// <summary>
    /// Gets the raw string replacement snippet, if initialized with a string; otherwise, <see langword="null"/>.
    /// </summary>
    public readonly string? StringValue;

    /// <summary>
    /// Gets the Roslyn syntax node replacement, if initialized with an AST node; otherwise, <see langword="null"/>.
    /// </summary>
    public readonly SyntaxNode? NodeValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverrideValue"/> struct with a literal string substitution.
    /// </summary>
    /// <param name="stringValue">The replacement source string to emit.</param>
    public OverrideValue(string stringValue)
    {
        StringValue = stringValue;
        NodeValue = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OverrideValue"/> struct with a syntax node substitution.
    /// </summary>
    /// <param name="nodeValue">The replacement syntax node to be visited and emitted.</param>
    public OverrideValue(SyntaxNode nodeValue)
    {
        NodeValue = nodeValue;
        StringValue = null;
    }

    /// <summary>
    /// Emits the substitution value to the visitor's underlying source writer.
    /// </summary>
    /// <param name="visitor">The active syntax walker handling node emission.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="visitor"/> is <see langword="null"/>.</exception>
    public void WriteTo(RoslynToCudaVisitor visitor)
    {
        if (visitor is null)
        {
            throw new ArgumentNullException(nameof(visitor));
        }

        if (NodeValue != null)
        {
            visitor.Visit(NodeValue);
        }
        else if (StringValue != null)
        {
            visitor.Context.Emitter.Writer.Append(StringValue);
        }
    }
}
