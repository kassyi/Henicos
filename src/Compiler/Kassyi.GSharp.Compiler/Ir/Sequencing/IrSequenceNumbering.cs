using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// The sequence temporaries in the order a writer spells their declarations; the index of each is its number
/// (<c>__gsharp_seq_&lt;n&gt;</c>, counted per method as the syntax path counts them).
/// </summary>
/// <remarks>
/// A declaration takes its number before its initializer is spelled, so the temporaries of a sequence inside an
/// initializer come after the one it initializes and before the next declaration. The other nodes are spelled in the
/// order of their children (<see cref="IrNodeChildren.Of"/>): a receiver before the arguments, the arguments in
/// parameter order.
/// </remarks>
internal static class IrSequenceNumbering
{
    /// <summary>The declared temporaries under <paramref name="root"/> in the order a writer spells them.</summary>
    public static ImmutableArray<IrVariable> Of(IrNode root)
    {
        var temporaries = ImmutableArray.CreateBuilder<IrVariable>();
        Add(root, temporaries);
        return temporaries.ToImmutable();
    }

    private static void Add(IrNode node, ImmutableArray<IrVariable>.Builder temporaries)
    {
        if (node is IrSequence sequence)
        {
            foreach (var statement in sequence.SideEffects)
            {
                if (statement is IrLocalDeclaration declaration)
                {
                    temporaries.Add(declaration.Variable);
                }

                Add(statement, temporaries);
            }

            Add(sequence.Value, temporaries);
            return;
        }

        foreach (var child in IrNodeChildren.Of(node))
        {
            Add(child, temporaries);
        }
    }
}
