namespace Kassyi.GSharp.Compiler.Backends;

/// <summary>Whether a writer spells a node itself (not what is below it), and the name of the shape.</summary>
/// <param name="IsSupported">Whether the writer spells the node.</param>
/// <param name="Shape">
/// The shape the writer spells when <paramref name="IsSupported"/>; otherwise why it cannot spell the node.
/// </param>
internal readonly record struct IrSupport(bool IsSupported, string Shape)
{
    /// <summary>A node the writer spells as <paramref name="shape"/>.</summary>
    public static IrSupport Spelled(string shape) => new(true, shape);

    /// <summary>A node the writer cannot spell, for <paramref name="shape"/>.</summary>
    public static IrSupport Unspelled(string shape) => new(false, shape);
}
