using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>The words and literals that <see cref="IrPrinter"/> writes for operators, constants and reasons.</summary>
internal static class IrPrintText
{
    /// <summary>The prefix of an expression of a category: <c>ref </c>, <c>in </c>, <c>ptr </c>, or nothing.</summary>
    public static string Category(IrCategory category) => category switch
    {
        IrCategory.Location => "ref ",
        IrCategory.ReadOnlyLocation => "in ",
        IrCategory.Pointer => "ptr ",
        _ => ""
    };

    /// <summary>The modifier of a variable that holds a reference.</summary>
    public static string RefKindModifier(RefKind refKind) => refKind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In => "in ",
        RefKind.RefReadOnlyParameter => "ref readonly ",
        _ => ""
    };

    /// <summary>The prefix of an argument that the caller did not write.</summary>
    public static string ArgumentKind(IrArgumentKind kind) => kind switch
    {
        IrArgumentKind.DefaultValue => "default ",
        IrArgumentKind.ParamArray => "params ",
        _ => ""
    };

    /// <summary>A constant as a C# literal: <c>3</c>, <c>3u</c>, <c>1.5f</c>, <c>'a'</c>, <c>null</c>.</summary>
    public static string Constant(object? value) => value switch
    {
        null => "null",
        bool flag => flag ? "true" : "false",
        char character => "'" + character + "'",
        string text => SymbolDisplay.FormatLiteral(text, quote: true),
        uint number => number.ToString(CultureInfo.InvariantCulture) + "u",
        long number => number.ToString(CultureInfo.InvariantCulture) + "L",
        ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
        float number => number.ToString("R", CultureInfo.InvariantCulture) + "f",
        double number => number.ToString("R", CultureInfo.InvariantCulture) + "d",
        decimal number => number.ToString(CultureInfo.InvariantCulture) + "m",
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => "?"
    };

    /// <summary>The syntax node of an opaque leaf as its kind (<c>SimpleLambdaExpression</c>), not its text.</summary>
    public static string SyntaxKindName(SyntaxNode syntax) => syntax.Kind().ToString();

    /// <summary>The name of an opaque reason.</summary>
    public static string Reason(IrOpaqueReason reason) => reason switch
    {
        IrOpaqueReason.Invalid => "Invalid",
        IrOpaqueReason.UnsupportedOperation => "UnsupportedOperation",
        IrOpaqueReason.Typeless => "Typeless",
        IrOpaqueReason.UnsupportedShape => "UnsupportedShape",
        _ => "NoOperation"
    };

    /// <summary>The kind of a conversion after the type, empty for a numeric or identity conversion.</summary>
    public static string ConversionKind(IrConversionKind kind) => kind switch
    {
        IrConversionKind.Identity or IrConversionKind.Numeric => "",
        IrConversionKind.ImplicitConstant => ":ImplicitConstant",
        IrConversionKind.Enumeration => ":Enumeration",
        IrConversionKind.Nullable => ":Nullable",
        IrConversionKind.Reference => ":Reference",
        IrConversionKind.Boxing => ":Boxing",
        IrConversionKind.Unboxing => ":Unboxing",
        IrConversionKind.UserDefined => ":UserDefined",
        IrConversionKind.Pointer => ":Pointer",
        _ => ":Other"
    };

    /// <summary>The C# text of a unary operator.</summary>
    public static string Unary(UnaryOperatorKind kind) => kind switch
    {
        UnaryOperatorKind.BitwiseNegation => "~",
        UnaryOperatorKind.Not => "!",
        UnaryOperatorKind.Plus => "+",
        UnaryOperatorKind.Minus => "-",
        UnaryOperatorKind.True => "true ",
        UnaryOperatorKind.False => "false ",
        UnaryOperatorKind.Hat => "^",
        _ => "? "
    };

    /// <summary>The C# text of a binary operator.</summary>
    public static string Binary(BinaryOperatorKind kind) => kind switch
    {
        BinaryOperatorKind.Add => "+",
        BinaryOperatorKind.Subtract => "-",
        BinaryOperatorKind.Multiply => "*",
        BinaryOperatorKind.Divide => "/",
        BinaryOperatorKind.Remainder => "%",
        BinaryOperatorKind.LeftShift => "<<",
        BinaryOperatorKind.RightShift => ">>",
        BinaryOperatorKind.UnsignedRightShift => ">>>",
        BinaryOperatorKind.And => "&",
        BinaryOperatorKind.Or => "|",
        BinaryOperatorKind.ExclusiveOr => "^",
        BinaryOperatorKind.ConditionalAnd => "&&",
        BinaryOperatorKind.ConditionalOr => "||",
        BinaryOperatorKind.Equals => "==",
        BinaryOperatorKind.NotEquals => "!=",
        BinaryOperatorKind.LessThan => "<",
        BinaryOperatorKind.LessThanOrEqual => "<=",
        BinaryOperatorKind.GreaterThan => ">",
        BinaryOperatorKind.GreaterThanOrEqual => ">=",
        _ => "?"
    };
}
