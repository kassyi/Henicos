namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>What a type test or <c>as</c> is written as (<see cref="TypeTests.Plan"/>).</summary>
internal enum TypeTestKind
{
    /// <summary>The constant that the types decide; the operand is still evaluated.</summary>
    Constant,

    /// <summary>Whether the operand is not null: its array or nullable has a value.</summary>
    NotNull,

    /// <summary>The operand itself: <c>as</c> with an identity conversion gives the value, null as null.</summary>
    Operand,

    /// <summary>A form that needs run-time type information or a heap object (GSH0003).</summary>
    Unsupported
}
