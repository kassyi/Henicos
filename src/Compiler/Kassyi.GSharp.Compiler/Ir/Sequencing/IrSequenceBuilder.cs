using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// Collects the temporaries of one <see cref="IrSequence"/> in the order they are declared, which is the order the
/// syntax path declares its <c>__gsharp_seq_&lt;n&gt;</c> temporaries in.
/// </summary>
/// <remarks>
/// A read of a temporary that replaces an operand takes the spelling of the operand (its span, and whether it holds a
/// <c>!</c>), without its parentheses, which stay on the initializer: the error sites the decisions took from the
/// operand do not move.
/// </remarks>
internal sealed class IrSequenceBuilder
{
    private readonly ImmutableArray<IrVariable>.Builder _locals = ImmutableArray.CreateBuilder<IrVariable>();
    private readonly ImmutableArray<IrStatement>.Builder _declarations = ImmutableArray.CreateBuilder<IrStatement>();

    /// <summary>
    /// Declares a temporary of <paramref name="type"/> that holds <paramref name="initializer"/> and returns a read of
    /// it used as <paramref name="use"/>.
    /// </summary>
    public IrVariableReference Spill(IrExpression initializer, ITypeSymbol type, RefKind refKind,
        IrDeclaredTypeForm form, IrCategory use)
    {
        var source = initializer.Source.AsSynthesized();
        var variable = IrVariable.CreateTemporary(type, refKind, "seq", source, form);
        _locals.Add(variable);
        _declarations.Add(new IrLocalDeclaration(variable, initializer, source));
        return Read(variable, use, initializer);
    }

    /// <summary>
    /// Declares a temporary of the value of <paramref name="operand"/> below its implicit built-in conversions, and
    /// returns the operand with that value replaced by a read of the temporary (the conversions stay).
    /// </summary>
    public IrExpression SpillValue(IrExpression operand)
    {
        var inner = IrStayInPlace.WithoutBuiltInImplicitConversions(operand);
        var read = Spill(inner, inner.Type, RefKind.None, IrDeclaredTypeForm.Deduced, inner.Category);
        return ReplaceBelowConversions(operand, read);
    }

    /// <summary>
    /// Declares a temporary of the whole <paramref name="operand"/>, named as a type when the syntax path writes
    /// <paramref name="declaredAs"/> for it (<see cref="DeclaredFormOf"/>), and returns a read of it.
    /// </summary>
    public IrVariableReference SpillConverted(IrExpression operand, ITypeSymbol? declaredAs) =>
        Spill(operand, operand.Type, RefKind.None, DeclaredFormOf(declaredAs), operand.Category);

    /// <summary>
    /// Declares a reference (<paramref name="refKind"/>: <see cref="RefKind.Ref"/>, or <see cref="RefKind.In"/> for a
    /// read-only one) to the location <paramref name="location"/> names and returns a read of it in the place of the
    /// location.
    /// </summary>
    public IrVariableReference SpillReference(IrExpression location, RefKind refKind = RefKind.Ref)
    {
        var initializer = AsLocation(location);
        return Spill(initializer, initializer.Type, refKind, IrDeclaredTypeForm.Deduced, location.Category);
    }

    /// <summary>
    /// Declares a copy of the value of the reference <paramref name="reference"/> holds, of <paramref name="type"/>.
    /// </summary>
    public void SpillCopy(IrVariableReference reference, ITypeSymbol type)
    {
        var read = IrExpression.WithSpelling(
            new IrVariableReference(reference.Variable, false, IrCategories.ForRead(type), default, reference.Source),
            0, reference.SpellingSpan, reference.SpellingHasNullForgiving);
        Spill(read, type, RefKind.None, IrDeclaredTypeForm.Deduced, read.Category);
    }

    /// <summary>
    /// The sequence of the declarations so far and <paramref name="value"/>, with the category, source and spelling of
    /// <paramref name="original"/>; its parentheses move from the value to the sequence.
    /// </summary>
    public IrSequence Build(IrExpression value, IrExpression original)
    {
        var sequence = new IrSequence(_locals.ToImmutable(), _declarations.ToImmutable(),
            IrExpression.WithParentheses(value, 0), original.Category, original.Source);
        return IrExpression.WithSpelling(sequence, original.Parentheses, original.SpellingSpan,
            original.SpellingHasNullForgiving);
    }

    /// <summary>
    /// How the syntax path declares a value temporary given <paramref name="declaredAs"/>: named for a type C++ can
    /// spell, deduced (<c>auto</c>) without a type, for an enum, a type parameter or a type without a direct C++ type.
    /// </summary>
    public static IrDeclaredTypeForm DeclaredFormOf(ITypeSymbol? declaredAs) =>
        declaredAs is not { TypeKind: TypeKind.Enum } && CppTypeMapping.HasDirectCppType(declaredAs)
            ? IrDeclaredTypeForm.Named
            : IrDeclaredTypeForm.Deduced;

    /// <summary>
    /// <paramref name="expression"/> as a location: a variable read as a value becomes a reference to the variable;
    /// any other expression stays as it is.
    /// </summary>
    public static IrExpression AsLocation(IrExpression expression) => expression is IrVariableReference
    {
        Category: not (IrCategory.Location or IrCategory.ReadOnlyLocation)
    } reference
        ? IrExpression.WithSpelling(
            new IrVariableReference(reference.Variable, false, IrCategory.Location, reference.ConstantValue,
                reference.Source),
            reference.Parentheses, reference.SpellingSpan, reference.SpellingHasNullForgiving)
        : expression;

    // <operand> with the expression below its implicit built-in conversions replaced by <replacement>
    private static IrExpression ReplaceBelowConversions(IrExpression operand, IrExpression replacement) =>
        operand is IrConversion { IsImplicit: true, Conversion.Method: null } conversion
            ? conversion.Update(ReplaceBelowConversions(conversion.Operand, replacement))
            : replacement;

    // A read of <variable> that takes the place, and the spelling, of <replaced>
    private static IrVariableReference Read(IrVariable variable, IrCategory use, IrExpression replaced) =>
        IrExpression.WithSpelling(
            new IrVariableReference(variable, false, use, default, replaced.Source.AsSynthesized()),
            0, replaced.SpellingSpan, replaced.SpellingHasNullForgiving);
}
