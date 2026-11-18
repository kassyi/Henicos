namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>How an <see cref="Expressions.IrConversion"/> is written.</summary>
internal enum IrConversionForm
{
    /// <summary>An implicit conversion that writes no cast of its own; the enclosing node decides any cast.</summary>
    Transparent,

    /// <summary>An explicit C++ cast to the target type.</summary>
    Cast,

    /// <summary>A float or double to integer conversion through a Prelude saturating helper.</summary>
    FloatToInteger,

    /// <summary>An explicit conversion folded to its constant value.</summary>
    Folded,

    /// <summary>A conversion form that the IR writers do not support yet and leave to the syntax path.</summary>
    Unsupported
}
