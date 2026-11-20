namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Classification of an <see cref="Expressions.IrPropertyReference"/> node (#1561).</summary>
internal enum IrMemberForm
{
    /// <summary>Mapped by the native <see cref="ITranspilerDialect"/> as an intrinsic property.</summary>
    DialectIntrinsic,

    /// <summary>
    /// An instance member of <c>System.Array</c> or <c>ImmutableArray&lt;T&gt;</c> that reads the array.
    /// </summary>
    ArrayMember,

    /// <summary>Constant <c>Length</c> of a direct stack array local reference.</summary>
    StackArrayLength,

    /// <summary>
    /// An instance property with accessor functions of a struct that the native <see cref="ITranspilerDialect"/>
    /// writes under its encoded name, wherever the property is used (a read, or the target of an assignment or a
    /// step); a read calls its getter.
    /// </summary>
    Getter,

    /// <summary>Not handled as one of the forms above (fallback or later passes).</summary>
    Other
}
