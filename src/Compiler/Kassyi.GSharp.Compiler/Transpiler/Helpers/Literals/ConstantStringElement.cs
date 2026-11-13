namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Literals;

/// <summary>
/// Writes an element of a constant string (<c>"\u00e9"[0]</c>, <c>Text[i]</c> of a <c>const string</c>,
/// <c>nameof(T)[0]</c>) as the UTF-16 code unit C# reads: a constant index is the <see langword="char"/> value itself,
/// a run-time index reads a <c>char16_t</c> literal of the string through <c>__gsharp_string_char</c>, which throws
/// <see cref="IndexOutOfRangeException"/> outside the string. A narrow C++ string would hold UTF-8 bytes, so a
/// non-ASCII character would read as one of its bytes (<c>"\u00e9"[0]</c> as -61).
/// </summary>
/// <remarks>
/// A constant index outside the string takes the run-time path and throws where it runs: the access may sit in a
/// branch that never runs (<c>if (T.Length &gt; 10) r[0] = T[10];</c>, where <c>T.Length</c> is not a C# constant),
/// which is correct C#. Any index of a constant that is <see langword="null"/> is a failure (GSH0003), as
/// <see cref="ConstantStringLength"/> reports for <c>Length</c>.
/// </remarks>
internal static class ConstantStringElement
{
    /// <summary>The GSH0003 text for an element of a constant string that is <see langword="null"/>.</summary>
    public const string NullConstant = "Element of a null constant string (it throws NullReferenceException)";

    private enum Shape : byte
    {
        /// <summary>Not an element of a constant string: the caller writes the access.</summary>
        None,

        /// <summary>The constant is <see langword="null"/>.</summary>
        NullConstant,

        /// <summary>A constant index: the code unit <see cref="Plan.Value"/>.</summary>
        ConstantElement,

        /// <summary>A run-time index into <see cref="Plan.Text"/>, or a constant one outside it.</summary>
        RuntimeIndex,
    }

    private readonly record struct Plan(
        Shape Shape,
        string Text = "",
        char Value = '\0',
        ExpressionSyntax? Index = null);

    /// <summary>Writes <paramref name="node"/> when it is an element of a constant string.</summary>
    /// <returns>Whether <paramref name="node"/> is such an element and was written.</returns>
    public static bool TryAppend(ElementAccessExpressionSyntax node, ref TranspileContext context)
    {
        var plan = PlanAccess(node, context.Query.SemanticModel);
        var writer = context.Emitter.Writer;
        switch (plan.Shape)
        {
            case Shape.None:
                return false;
            case Shape.NullConstant:
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, NullConstant);
                context.Visitor.AppendUnsupportedPlaceholder(node);
                return true;
            case Shape.ConstantElement:
                // The node's own type is char; ConstantAtUse writes the type of an implicit conversion around it
                var type = context.Query.SemanticModel.GetTypeSymbol(node) ??
                           context.Query.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Char);
                ConstantAtUse.Append(node, type, plan.Value, ref context);
                return true;
            case Shape.RuntimeIndex when plan.Index is { } index:
                // __gsharp_string_char(u"<text>", <index>)
                writer.Append(GSharpInternalNames.Prelude.StringChar);
                using (writer.Parentheses())
                {
                    CppStringLiteral.AppendUtf16(writer, plan.Text);
                    writer.Append(',');
                    context.Visitor.Visit(index);
                }

                return true;
            default:
                throw new InvalidOperationException($"Unknown constant string element shape {plan.Shape}.");
        }
    }

    // [WHY] Only the int indexer of string (Chars): an Index (s[^1]) or a Range (s[1..]) binds to another member, and
    // its argument is not an int
    private static Plan PlanAccess(ElementAccessExpressionSyntax node, SemanticModel model)
    {
        if (node.ArgumentList.Arguments is not [var argument] ||
            model.GetTypeSymbol(node.Expression) is not { SpecialType: SpecialType.System_String } ||
            model.GetConvertedTypeSymbol(argument.Expression) is not { SpecialType: SpecialType.System_Int32 } ||
            model.GetSafeConstantValue(node.Expression) is not { HasValue: true } constant)
        {
            return new Plan(Shape.None);
        }

        if (constant.Value is not string text)
        {
            return new Plan(Shape.NullConstant);
        }

        if (model.GetSafeConstantValue(argument.Expression) is not { HasValue: true, Value: { } index })
        {
            return new Plan(Shape.RuntimeIndex, text, Index: argument.Expression);
        }

        var position = Convert.ToInt64(index, System.Globalization.CultureInfo.InvariantCulture);
        return position >= 0 && position < text.Length
            ? new Plan(Shape.ConstantElement, Value: text[(int)position])
            : new Plan(Shape.RuntimeIndex, text, Index: argument.Expression);
    }
}
