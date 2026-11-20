namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// The plan of one form: the operands that go into temporaries, in the C# order of evaluation. A form that needs no
/// sequence returns <see cref="None"/>; a plan that was never made (<see langword="default"/>) is an internal error.
/// </summary>
/// <param name="Temps">The operands that go into temporaries, by ascending operand index.</param>
/// <param name="CopiesTarget">
/// Whether a compound assignment copies the current value of its target before the right side runs.
/// </param>
internal readonly record struct IrSequencePlan(ImmutableArray<IrSequenceTempPlan> Temps, bool CopiesTarget = false)
{
    /// <summary>The plan of a form whose operands stay in place.</summary>
    public static IrSequencePlan None { get; } = new(ImmutableArray<IrSequenceTempPlan>.Empty);

    /// <summary>Whether the form becomes a sequence.</summary>
    /// <exception cref="InvalidOperationException">The plan was never made.</exception>
    public bool IsSequenced => Temps.IsDefault
        ? throw new InvalidOperationException("A sequence plan was used before it was made.")
        : !Temps.IsEmpty;

    /// <summary>The kind of the temporary of operand <paramref name="operandIndex"/>.</summary>
    /// <exception cref="InvalidOperationException">The plan leaves that operand in place.</exception>
    public IrSequenceTempKind KindOf(int operandIndex)
    {
        foreach (var temp in Temps)
        {
            if (temp.OperandIndex == operandIndex)
            {
                return temp.Kind;
            }
        }

        throw new InvalidOperationException($"The sequence plan leaves operand {operandIndex} in place.");
    }

    /// <summary>The plan of a form whose operands all go into temporaries of the given kinds, in order.</summary>
    public static IrSequencePlan All(params IrSequenceTempKind[] kinds)
    {
        var temps = ImmutableArray.CreateBuilder<IrSequenceTempPlan>(kinds.Length);
        for (var index = 0; index < kinds.Length; index++)
        {
            temps.Add(new IrSequenceTempPlan(index, kinds[index]));
        }

        return new IrSequencePlan(temps.MoveToImmutable());
    }
}
