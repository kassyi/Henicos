namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// How <see langword="this"/> is spelled inside the struct member or lowered function currently being emitted.
/// </summary>
internal enum SelfForm : byte
{
    /// <summary>A C++ struct member function (property accessor or <c>Dispose</c>): <c>this</c> is a C++ pointer.</summary>
    MemberFunction,

    /// <summary>A lowered struct constructor factory function: <c>__gsharp_self</c> is a local struct value.</summary>
    ConstructorSelf,

    /// <summary>
    /// A static C++ device function for a struct instance method: <c>__gsharp_this</c> is an explicit pointer
    /// parameter.
    /// </summary>
    ReceiverParameter
}
