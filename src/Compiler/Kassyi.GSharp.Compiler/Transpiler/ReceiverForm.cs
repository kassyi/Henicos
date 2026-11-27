namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>How the receiver of a call is passed to a C++ free function.</summary>
internal enum ReceiverForm : byte
{
    /// <summary>A mutable or <see langword="readonly"/>-callee variable whose address is passed directly.</summary>
    Address,

    /// <summary>A readonly variable passed to a non-<see langword="readonly"/> method on a defensive copy.</summary>
    Copy,

    /// <summary>A value (rvalue) receiver passed on a temporary.</summary>
    Temporary,

    /// <summary>An implicit or explicit <see langword="this"/> passed through the enclosing body's self pointer.</summary>
    Self,

    /// <summary>A receiver passed by value (StackLinq, MemoryExtensions, or by-value extension method).</summary>
    Value
}
