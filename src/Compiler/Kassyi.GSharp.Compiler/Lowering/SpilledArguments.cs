using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The rule for what of the arguments of a call that may suspend (or a warp shuffle) the lowering evaluates once into a
/// temporary before the call, and what the call reads itself, again on every resume.
/// <see cref="WarpInstructionRewriter"/> spills by it, and the liveness of the cross-barrier rules (GSH0039) reads it,
/// so both agree on every argument.
/// </summary>
/// <remarks>
/// [WHY] What C# evaluates once, when it evaluates the argument, must not be evaluated again at the call: the spilled
/// arguments run before it, and a suspended call evaluates what stays in place again on every resume, after the callee
/// may have written through its ref / out arguments. A by-value argument, an index or the array of a by-reference
/// argument stays in place only when it is a literal, a constant, or a local or by-value parameter that no other name
/// reaches (no ref local, ref parameter or taken address) and that no other argument of the call writes, before or
/// after it, also by passing it by ref / out (#788); a span or other ref struct, which no temporary can keep across the
/// split, stays in place unless a later argument writes it. A by-reference argument (ref, out, in, or an in parameter
/// given a variable without the keyword) is not copied: only what selects its location is evaluated once.
/// </remarks>
internal static class SpilledArguments
{
    /// <summary>
    /// Whether the by-value argument at <paramref name="index"/> is evaluated into a temporary before the call; false
    /// for a by-reference argument (<see cref="IsPassedByReference"/>) and an <c>out</c> declaration.
    /// </summary>
    public static bool IsSpilledArgument(SeparatedSyntaxList<ArgumentSyntax> arguments, int index, SemanticModel? model)
    {
        var argument = arguments[index];
        return argument.Expression is not DeclarationExpressionSyntax &&
               !IsPassedByReference(argument, model) &&
               !CanStayInPlace(argument.Expression, arguments, index, model);
    }

    /// <summary>
    /// Whether <paramref name="use"/>, inside the location of the by-reference argument at <paramref name="index"/>, is
    /// in a part that is evaluated into a temporary before the call: an index, the array or pointer of an element, or
    /// the pointer of <c>*p</c> and <c>p-&gt;F</c>.
    /// </summary>
    public static bool IsInSpilledLocationPart(ExpressionSyntax location, SyntaxNode use,
        SeparatedSyntaxList<ArgumentSyntax> arguments, int index, SemanticModel? model)
    {
        switch (location)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return IsInSpilledLocationPart(parenthesized.Expression, use, arguments, index, model);
            case MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } member:
                return IsInSpilledLocationPart(member.Expression, use, arguments, index, model);
            case MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.PointerMemberAccessExpression } pointerMember:
                return !CanStayInPlace(pointerMember.Expression, arguments, index, model);
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PointerIndirectionExpression } indirection:
                return !CanStayInPlace(indirection.Operand, arguments, index, model);
            case ElementAccessExpressionSyntax elementAccess:
                if (elementAccess.Expression.Span.Contains(use.Span))
                {
                    return IsSpilledReceiver(elementAccess.Expression, arguments, index, model) ||
                           IsInSpilledLocationPart(elementAccess.Expression, use, arguments, index, model);
                }

                return elementAccess.ArgumentList.Arguments
                    .AsStackEnumerable()
                    .Any((Use: use, Arguments: arguments, Index: index, Model: model), static (elementIndex, s) =>
                        elementIndex.Span.Contains(s.Use.Span) &&
                        !CanStayInPlace(elementIndex.Expression, s.Arguments, s.Index, s.Model)
                    );
            default:
                return false;
        }
    }

    /// <summary>
    /// Whether the receiver of an element access in a by-reference argument is evaluated into a temporary: an array or
    /// a pointer (<see cref="IsValueReceiver"/>) that cannot stay in place. Without a semantic model no receiver is.
    /// </summary>
    public static bool IsSpilledReceiver(ExpressionSyntax receiver, SeparatedSyntaxList<ArgumentSyntax> arguments,
        int index, SemanticModel? model) =>
        model != null &&
        IsValueReceiver(receiver, model) &&
        !CanStayInPlace(receiver, arguments, index, model);

    /// <summary>
    /// Whether the receiver of an element access is a value that selects the element: an array (a reference) or a
    /// pointer, which C# evaluates once when it evaluates the argument. A stack array, a span, an inline array or a
    /// fixed-size buffer holds the elements and is itself a location.
    /// </summary>
    public static bool IsValueReceiver(ExpressionSyntax receiver, SemanticModel model) =>
        model.GetTypeSymbol(receiver) switch
        {
            IArrayTypeSymbol => !StackArrayLocals.IsStackArrayReference(receiver, model),
            IPointerTypeSymbol => model.GetSymbol(receiver) is not IFieldSymbol { IsFixedSizeBuffer: true },
            _ => false
        };

    /// <summary>
    /// Whether an argument is passed by reference on the CPU paths (<see cref="ClassifyByReference"/>).
    /// </summary>
    public static bool IsPassedByReference(ArgumentSyntax argument, SemanticModel? model) =>
        ClassifyByReference(argument, model) != ByReferencePassing.Value;

    /// <summary>
    /// How the CPU lowering passes an argument of a call that may suspend: by value; as a location that it selects once
    /// before the call (a ref, out or in argument, or a variable given to an in / ref readonly parameter without the
    /// keyword, which C# passes by reference too); or as a location that a call, a property or a ref conditional
    /// selects, which every resume would select again (GSH0062).
    /// </summary>
    // [WHY] One answer for the lowering, the liveness of the cross-barrier rules and GSH0062, so that what the analyzer
    // accepts is what the lowering keeps. A span element without the keyword stays a copy: the span is not kept across
    // a resume (#990)
    public static ByReferencePassing ClassifyByReference(ArgumentSyntax argument, SemanticModel? model)
    {
        var hasKeyword = !argument.RefKindKeyword.IsKind(SyntaxKind.None);
        if (!hasKeyword && !IsVariableOfReadOnlyReference(argument, model))
        {
            return ByReferencePassing.Value;
        }

        if (model == null)
        {
            return ByReferencePassing.Location;
        }

        return RootOf(argument.Expression, model) switch
        {
            LocationRoot.SpanElement when !hasKeyword => ByReferencePassing.Value,
            LocationRoot.Selected => ByReferencePassing.ReselectedLocation,
            _ => ByReferencePassing.Location
        };
    }

    // An argument of an in / ref readonly parameter that names a variable. The operation of the expression is the
    // argument's value only when no conversion sits between them; a field of a value is a value, which is copied.
    // Parentheses have no operation of their own, so the variable inside them is looked up.
    private static bool IsVariableOfReadOnlyReference(ArgumentSyntax argument, SemanticModel? model) =>
        model?.GetSafeOperation(IteratorTypeResolver.UnwrapParentheses(argument.Expression)) is
        {
            Parent: IArgumentOperation { Parameter.RefKind: RefKind.In or RefKind.RefReadOnlyParameter }
        } operation &&
        operation.IsVariable();

    private enum LocationRoot
    {
        Kept,
        SpanElement,
        Selected
    }

    // What the variable of a location hangs from. A local, a parameter, this, or an array or pointer value (evaluated
    // once) is kept; a call, a property (an indexer included) or a ref conditional selects the variable and would be
    // evaluated again on every resume (#927 is to keep the reference itself instead)
    private static LocationRoot RootOf(ExpressionSyntax location, SemanticModel model) => location switch
    {
        ParenthesizedExpressionSyntax parenthesized => RootOf(parenthesized.Expression, model),
        MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.PointerMemberAccessExpression } => LocationRoot.Kept,
        MemberAccessExpressionSyntax member => model.GetSymbol(member) is IPropertySymbol
            ? LocationRoot.Selected
            : RootOf(member.Expression, model),
        ElementAccessExpressionSyntax elementAccess => RootOfElement(elementAccess, model),
        PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PointerIndirectionExpression } => LocationRoot.Kept,
        IdentifierNameSyntax identifier => model.GetSymbol(identifier) is IPropertySymbol
            ? LocationRoot.Selected
            : LocationRoot.Kept,
        ThisExpressionSyntax or BaseExpressionSyntax or DeclarationExpressionSyntax => LocationRoot.Kept,
        _ => LocationRoot.Selected
    };

    private static LocationRoot RootOfElement(ElementAccessExpressionSyntax elementAccess, SemanticModel model)
    {
        if (IsValueReceiver(elementAccess.Expression, model))
        {
            return LocationRoot.Kept;
        }

        return model.GetSymbol(elementAccess) switch
        {
            IPropertySymbol { ContainingType.IsRefLikeType: true } => LocationRoot.SpanElement,
            IPropertySymbol => LocationRoot.Selected,
            _ => RootOf(elementAccess.Expression, model)
        };
    }

    /// <summary>
    /// Whether <paramref name="expression"/> (a by-value argument, an index or the array of a by-reference argument at
    /// <paramref name="index"/>) can be read at the call: a literal, a constant, or an unaliased local or by-value
    /// parameter that no other argument writes.
    /// </summary>
    public static bool CanStayInPlace(ExpressionSyntax expression, SeparatedSyntaxList<ArgumentSyntax> arguments,
        int index, SemanticModel? model) =>
        expression is LiteralExpressionSyntax ||
        (model != null && model.GetSafeConstantValue(expression).HasValue) ||
        (expression is IdentifierNameSyntax identifier &&
         (IsRefLike(identifier, model)
             ? !IsWrittenByLaterArgument(identifier, arguments, index, model)
             : IsUnaliasedVariable(identifier, model) &&
               !IsWrittenByOtherArgument(identifier, arguments, index, model)));

    /// <summary>
    /// Whether the left operand of <paramref name="binary"/>, whose right operand holds a call that splits, is
    /// evaluated into a temporary before the split, as C# evaluates it before the right operand (#1535). Not for
    /// <c>&amp;&amp;</c>, <c>||</c> and <c>??</c>, whose right operand runs only on one branch, nor for a left operand
    /// that can be read after the split: a literal, a constant, or a variable that the right operand does not write (an
    /// unaliased local or by-value parameter; a span or other ref struct, which no temporary keeps across the split,
    /// unless the right operand writes it).
    /// </summary>
    public static bool IsSpilledLeftOperand(BinaryExpressionSyntax binary, SemanticModel? model)
    {
        if (binary.IsKind(SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression,
                SyntaxKind.CoalesceExpression))
        {
            return false;
        }

        var left = binary.Left;
        if (left is LiteralExpressionSyntax || (model != null && model.GetSafeConstantValue(left).HasValue))
        {
            return false;
        }

        if (left is not IdentifierNameSyntax identifier)
        {
            return true;
        }

        if (model == null)
        {
            return WritesByName(binary.Right, identifier.Identifier.ValueText);
        }

        var written = ExpressionEffects.Of(identifier, model).InterferesWith(ExpressionEffects.Of(binary.Right, model));
        return IsRefLike(identifier, model) ? written : written || !IsUnaliasedVariable(identifier, model);
    }

    // [WHY] A span or another ref struct cannot be kept in a temporary across the split (it is declared again on every
    // resume, ResumeLocals), so evaluating it into one does not fix its value. It stays in place, and the cross-barrier
    // rule (GSH0039) reports it when it is changed before the split and read by the call when it resumes; only a write
    // by a later argument spills it, as before #850 (#608)
    private static bool IsRefLike(IdentifierNameSyntax identifier, SemanticModel? model) =>
        model?.GetTypeSymbol(identifier) is { IsRefLikeType: true };

    // Whether an argument after index writes the variable of identifier (the rule of #608, kept for ref structs)
    private static bool IsWrittenByLaterArgument(IdentifierNameSyntax identifier,
        SeparatedSyntaxList<ArgumentSyntax> arguments, int index, SemanticModel? model)
    {
        if (model == null)
        {
            return arguments
                .AsStackEnumerable()
                .Skip(index + 1)
                .Any(identifier.Identifier.ValueText, static (later, name) => WritesByName(later, name));
        }

        var read = ExpressionEffects.Of(identifier, model);
        return arguments
            .AsStackEnumerable()
            .Skip(index + 1)
            .Any((Read: read, Model: model), static (later, s) => s.Read.InterferesWith(
                later.RefKindKeyword.IsKind(SyntaxKind.None)
                    ? ExpressionEffects.Of(later.Expression, s.Model)
                    : ExpressionEffects.OfLocation(later.Expression, s.Model)
            ));
    }

    // A local or by-value parameter that has no other name. A name without a symbol is a temporary of lowering.
    private static bool IsUnaliasedVariable(IdentifierNameSyntax identifier, SemanticModel? model) =>
        model == null ||
        model.GetSymbol(identifier) switch
        {
            null => true,
            ILocalSymbol { IsRef: false } local => !AliasedVariables.Contains(local, model.Compilation),
            IParameterSymbol { RefKind: RefKind.None } parameter =>
                !AliasedVariables.Contains(parameter, model.Compilation),
            _ => false
        };

    // [WHY] C# reads an argument before it evaluates the next one, but an identifier that is not spilled is read at the
    // call, after the spilled arguments, and again on every resume: one whose variable another argument writes
    // ('S(a, Next(ref a))', 'S(ref a[i], i++)', 'S(ref x, x)' with a callee that writes x) is spilled as well, so that
    // it keeps the value C# reads (#608). An argument passed by ref / out writes its variable.
    private static bool IsWrittenByOtherArgument(IdentifierNameSyntax identifier,
        SeparatedSyntaxList<ArgumentSyntax> arguments, int index, SemanticModel? model)
    {
        if (model == null)
        {
            return arguments
                .AsStackEnumerable()
                .Skip(index + 1)
                .Any(identifier.Identifier.ValueText, static (later, name) => WritesByName(later, name));
        }

        var read = ExpressionEffects.Of(identifier, model);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (i != index && read.InterferesWith(GetArgumentEffects(arguments[i], model)))
            {
                return true;
            }
        }

        return false;
    }

    // What evaluating an argument and passing it reads and writes: a ref / out argument also writes its variable, which
    // the callee may change before it suspends
    private static ExpressionEffects GetArgumentEffects(ArgumentSyntax argument, SemanticModel model)
    {
        if (argument.RefKindKeyword.IsKind(SyntaxKind.None) || argument.RefKindKeyword.IsKind(SyntaxKind.InKeyword))
        {
            return argument.RefKindKeyword.IsKind(SyntaxKind.None)
                ? ExpressionEffects.Of(argument.Expression, model)
                : ExpressionEffects.OfLocation(argument.Expression, model);
        }

        return ExpressionEffects.OfLocation(argument.Expression, model)
            .Union(new ExpressionEffects(default, ExpressionEffects.RootOf(argument.Expression, model)));
    }

    // Without a semantic model: whether argument holds a ref / out argument, an assignment or an increment of the
    // variable named name
    private static bool WritesByName(SyntaxNode argument, string name) =>
        argument
            .DescendantNodes()
            .AsStackEnumerable()
            .Any(name, static (node, n) => node switch
            {
                ArgumentSyntax { RefKindKeyword.RawKind: not (int)SyntaxKind.None } byRef =>
                    IsNamed(byRef.Expression, n),
                AssignmentExpressionSyntax assignment => IsNamed(assignment.Left, n),
                PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression,
                    SyntaxKind.PreDecrementExpression) => IsNamed(prefix.Operand, n),
                PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression,
                    SyntaxKind.PostDecrementExpression) => IsNamed(postfix.Operand, n),
                _ => false
            });

    private static bool IsNamed(ExpressionSyntax expression, string name) =>
        expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == name;
}
