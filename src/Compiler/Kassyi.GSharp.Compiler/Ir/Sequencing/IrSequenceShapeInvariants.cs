using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// Checks S3 and S4 of <see cref="IrSequenceInvariants"/>: the shapes of a sequence that a writer reads without a
/// field that names them.
/// </summary>
internal static class IrSequenceShapeInvariants
{
    /// <summary>
    /// S3 (a): an index temporary of an element sequence holds the index as written, which a writer may wrap in its
    /// overflow check, not a read of another temporary.
    /// </summary>
    public static void CheckIndex(IrSequence sequence, ImmutableArray<string>.Builder violations)
    {
        var element = sequence.Value switch
        {
            IrElementReference read => read,
            IrAssignment { Target: IrElementReference target } => target,
            _ => null
        };
        if (element is null ||
            IrStayInPlace.WithoutBuiltInImplicitConversions(element.Index) is not IrVariableReference index ||
            DeclarationOf(sequence, index.Variable) is not { } declaration)
        {
            return;
        }

        if (declaration.Initializer is IrVariableReference { Variable.Kind: IrVariableKind.Temporary })
        {
            IrSequenceInvariants.Add(violations, "S3", declaration,
                "an index temporary holds a read of another temporary, not the index");
        }
    }

    /// <summary>
    /// S3 (b): a compound assignment over a reference to its target has the copy of the target exactly when it has
    /// three declarations (<see cref="IrSequenceSupport.HasTargetCopy"/>).
    /// </summary>
    public static void CheckTargetCopy(IrSequence sequence, ImmutableArray<string>.Builder violations)
    {
        if (sequence.Value is not IrCompoundAssignment { Target: IrVariableReference target } ||
            DeclarationOf(sequence, target.Variable) is not { Variable.RefKind: not RefKind.None })
        {
            return;
        }

        var hasCopy = IrSequenceSupport.HasTargetCopy(sequence);
        if (hasCopy != (sequence.SideEffects.Length == 3))
        {
            IrSequenceInvariants.Add(violations, "S3", sequence,
                "a compound assignment over its target has a copy of the target unless it has two declarations");
        }
    }

    /// <summary>
    /// S4: a temporary declared as a named type holds its operand converted, so no conversion applies to its reads.
    /// </summary>
    public static void CheckConvertedValues(IrSequence sequence, ImmutableArray<string>.Builder violations)
    {
        foreach (var node in IrNodeChildren.DescendantsAndSelf(sequence.Value))
        {
            if (node is IrConversion { Operand: IrVariableReference read } conversion &&
                read.Variable.DeclaredTypeForm == IrDeclaredTypeForm.Named &&
                DeclarationOf(sequence, read.Variable) is not null)
            {
                IrSequenceInvariants.Add(violations, "S4", conversion,
                    "a conversion applies to a temporary that holds its value converted");
            }
        }
    }

    private static IrLocalDeclaration? DeclarationOf(IrSequence sequence, IrVariable variable) =>
        sequence.SideEffects
            .AsStackEnumerable()
            .OfType<IrLocalDeclaration>()
            .FirstOrDefault(variable, static (declaration, wanted) => declaration.Variable == wanted);
}
