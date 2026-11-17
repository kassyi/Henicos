namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>A reference-identity label in the linear form of a method body.</summary>
/// <param name="kind">What the label marks.</param>
/// <param name="ordinal">The zero-based number within <paramref name="kind"/>.</param>
internal sealed class IrLabelSymbol(IrLabelKind kind, int ordinal)
{
    /// <summary>What the label marks.</summary>
    public IrLabelKind Kind { get; } = kind;

    /// <summary>The zero-based number within <see cref="Kind"/>.</summary>
    public int Ordinal { get; } = ordinal;

    /// <summary>The name the syntax lowering gives the same label: <c>__gsharp_BranchEnd_0</c>.</summary>
    /// <exception cref="InvalidOperationException">The label is a label of the user's code.</exception>
    public string Name => Kind == IrLabelKind.User
        ? throw new InvalidOperationException("A label of the user's code keeps its own name.")
        : GSharpInternalNames.Lowering.LabelPrefix + Kind.ToString() + "_" +
          Ordinal.ToString(CultureInfo.InvariantCulture);
}
