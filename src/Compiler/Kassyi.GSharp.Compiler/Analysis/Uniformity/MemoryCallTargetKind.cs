namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>How a call reaches the memory of a <see cref="MemoryCallTarget"/>.</summary>
internal enum MemoryCallTargetKind
{
    /// <summary>A <c>ref</c> or <c>out</c> argument that is a location of the memory (<c>ref buffer[0]</c>).</summary>
    ByReference,

    /// <summary>The receiver, which is the memory itself (<c>span.Fill(0)</c>).</summary>
    Receiver,

    /// <summary>
    /// The element (or a field of one) that a struct member is called on, which it may write through <c>this</c>
    /// (<c>a[i].Bump()</c>).
    /// </summary>
    ReceiverLocation,

    /// <summary>An argument that is the memory itself (an array, span or <c>SharedMemory&lt;T&gt;</c>).</summary>
    Argument,

    /// <summary>The memory of a field of a struct argument or receiver.</summary>
    Field
}
