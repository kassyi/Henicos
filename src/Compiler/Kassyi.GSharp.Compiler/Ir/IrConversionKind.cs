namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>What kind of C# conversion an <see cref="Expressions.IrConversion"/> performs.</summary>
internal enum IrConversionKind
{
    /// <summary>The operand already has the type (an explicit cast to the same type).</summary>
    Identity,

    /// <summary>A conversion between numeric types (C# §10.2.3, §10.3.2), <c>char</c> included.</summary>
    Numeric,

    /// <summary>An implicit conversion of a constant expression to a narrower integer type (<c>byte b =
    /// 1</c>).</summary>
    ImplicitConstant,

    /// <summary>A conversion from or to an enum type.</summary>
    Enumeration,

    /// <summary>A conversion to or from <c>Nullable&lt;T&gt;</c>.</summary>
    Nullable,

    /// <summary>A reference conversion.</summary>
    Reference,

    /// <summary>A boxing conversion.</summary>
    Boxing,

    /// <summary>An unboxing conversion.</summary>
    Unboxing,

    /// <summary>A user-defined conversion operator.</summary>
    UserDefined,

    /// <summary>A pointer conversion.</summary>
    Pointer,

    /// <summary>Any other conversion (a span conversion of an array, a method group, ...).</summary>
    Other
}
