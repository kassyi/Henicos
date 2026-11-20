namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>What a temporary of a sequence holds and how its initializer is taken from the operand.</summary>
internal enum IrSequenceTempKind
{
    /// <summary>
    /// The value of the operand below the implicit built-in conversions C# inserted, which stay at the use; the
    /// declaration takes the type of the initializer.
    /// </summary>
    Value,

    /// <summary>
    /// The value of the whole operand, implicit conversions included, declared as the type of the form.
    /// </summary>
    ConvertedValue,

    /// <summary>A reference to the location the operand names.</summary>
    Reference,

    /// <summary>A read-only reference to the location the operand names.</summary>
    ReadOnlyReference
}
