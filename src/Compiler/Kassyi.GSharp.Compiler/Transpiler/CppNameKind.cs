namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Selects how a <see cref="CppName"/> is spelled in emitted C++.</summary>
internal enum CppNameKind : byte
{
    /// <summary>Emitted unchanged: compiler-internal names, external names and names not yet renamed (#402).</summary>
    Verbatim,

    /// <summary>
    /// A parameter, local, label or local function: <c>__gsharp_id_{ordinal}_{name}</c>, or
    /// <c>__gsharp_il_{region}_{ordinal}_{name}</c> when <see cref="CppName.Region"/> is greater than zero.
    /// </summary>
    FunctionScope,

    /// <summary>An instance field or property of a struct that G# emits itself: <c>__gsharp_mb_{name}</c>.</summary>
    Member,

    /// <summary>
    /// The backing field of a property of such a struct whose accessors use the <c>field</c> keyword:
    /// <c>__gsharp_bf_{property name}</c>. The accessors keep the member name of the property.
    /// </summary>
    BackingField,

    /// <summary>
    /// A name in the global C++ scope, already complete: <c>__gsharp_ty_</c> / <c>__gsharp_tp_</c> / <c>__gsharp_fn_</c> (see
    /// <see cref="GlobalCppNames"/>).
    /// </summary>
    Global
}
