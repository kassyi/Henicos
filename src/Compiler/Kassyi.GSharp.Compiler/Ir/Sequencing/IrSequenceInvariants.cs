using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// Checks the invariants of the sequences of a tree after <see cref="IrSequencer"/>: S1 the temporaries are the
/// declarations, in order, each declared once; S2 a declaration keeps the type of its initializer and a reference is
/// initialized with a location; S3 and S4 the shapes a writer reads (<see cref="IrSequenceShapeInvariants"/>).
/// </summary>
internal static class IrSequenceInvariants
{
    /// <summary>The violations under <paramref name="root"/>; empty when all hold.</summary>
    public static ImmutableArray<string> Verify(IrNode root)
    {
        var violations = ImmutableArray.CreateBuilder<string>();
        var declared = new HashSet<IrVariable>();
        var spelledTwice = SequencesInTargetsSpelledTwice(root);
        foreach (var node in IrNodeChildren.DescendantsAndSelf(root))
        {
            if (node is not IrSequence sequence)
            {
                continue;
            }

            CheckDeclarations(sequence, declared, violations);
            if (!spelledTwice.Contains(sequence))
            {
                IrSequenceShapeInvariants.CheckIndex(sequence, violations);
            }

            IrSequenceShapeInvariants.CheckTargetCopy(sequence, violations);
            IrSequenceShapeInvariants.CheckConvertedValues(sequence, violations);
        }

        return violations.ToImmutable();
    }

    /// <summary>Adds a violation in the format <c>S&lt;n&gt; at &lt;start&gt;..&lt;end&gt;: &lt;rule&gt;</c>.</summary>
    public static void Add(ImmutableArray<string>.Builder violations, string code, IrNode node, string text) =>
        violations.Add($"{code} at {node.Source.Start}..{node.Source.Start + node.Source.Length}: {text}");

    // The sequences inside a target that a writer cannot spell yet (IrSequenceSupport.SpellsTargetTwiceWithSequence):
    // there an element reads the parts its target already moved into temporaries, so S3 (a) does not hold
    private static HashSet<IrSequence> SequencesInTargetsSpelledTwice(IrNode root)
    {
        var sequences = new HashSet<IrSequence>();
        foreach (var node in IrNodeChildren.DescendantsAndSelf(root))
        {
            var target = node switch
            {
                IrCompoundAssignment compound when IrSequenceSupport.SpellsTargetTwiceWithSequence(compound) =>
                    compound.Target,
                IrIncrement increment when IrSequenceSupport.SpellsTargetTwiceWithSequence(increment) =>
                    increment.Target,
                _ => null
            };
            if (target is not null)
            {
                IrNodeChildren.DescendantsAndSelf(target)
                    .AsStackEnumerable()
                    .OfType<IrSequence>()
                    .AddTo(sequences);
            }
        }

        return sequences;
    }

    // S1, S2
    private static void CheckDeclarations(IrSequence sequence, HashSet<IrVariable> declared,
        ImmutableArray<string>.Builder violations)
    {
        if (sequence.Locals.Length != sequence.SideEffects.Length)
        {
            Add(violations, "S1", sequence, "the temporaries are not the declarations");
        }

        for (var index = 0; index < sequence.SideEffects.Length; index++)
        {
            if (sequence.SideEffects[index] is not IrLocalDeclaration declaration)
            {
                Add(violations, "S1", sequence, "a statement of a sequence is not a declaration");
                continue;
            }

            var variable = declaration.Variable;
            if (index >= sequence.Locals.Length || sequence.Locals[index] != variable)
            {
                Add(violations, "S1", declaration, "a declaration is not the temporary at its position");
            }

            if (variable.Kind != IrVariableKind.Temporary || !declared.Add(variable))
            {
                Add(violations, "S1", declaration, "a variable is not a temporary declared once");
            }

            CheckInitializer(declaration, violations);
        }
    }

    // S2: the type is kept, and a reference refers to a location (an array bound by reference may be a value)
    private static void CheckInitializer(IrLocalDeclaration declaration, ImmutableArray<string>.Builder violations)
    {
        var variable = declaration.Variable;
        if (declaration.Initializer is not { } initializer)
        {
            Add(violations, "S2", declaration, "a temporary has no initializer");
            return;
        }

        if (!SymbolEqualityComparer.Default.Equals(initializer.Type, variable.Type))
        {
            Add(violations, "S2", declaration, "a temporary does not have the type of its initializer");
        }

        if (variable.RefKind != RefKind.None &&
            initializer.Category is not (IrCategory.Location or IrCategory.ReadOnlyLocation) &&
            initializer.Type is not IArrayTypeSymbol)
        {
            Add(violations, "S2", declaration, "a reference temporary is not initialized with a location");
        }
    }
}
