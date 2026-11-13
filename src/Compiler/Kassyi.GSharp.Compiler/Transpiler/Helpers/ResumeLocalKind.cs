namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>How a resume local (<see cref="ResumeLocals"/>) gets its storage and its value.</summary>
internal enum ResumeLocalKind
{
    /// <summary>
    /// Declared with a default value before the state switch; its initializer becomes an assignment where it is declared.
    /// </summary>
    Bound,

    /// <summary>
    /// Initialized with a stackalloc of a constant size: the memory is declared before the state switch, and where the
    /// local is declared the memory is zeroed again (or receives the elements of the initializer list).
    /// </summary>
    StackAlloc,

    /// <summary>
    /// Initialized with a stackalloc whose size is not a constant, or that is not the initializer itself (a cast): reported
    /// as a dynamic array size (GSH0003), as the declaration rule reports it without a barrier.
    /// </summary>
    Unsupported,

    /// <summary>
    /// A managed <c>SharedMemory&lt;T&gt;</c> (a C# class cannot hold it as a field): declared before the state switch
    /// with the initializer that <c>SharedMemorySiteKeyAnnotator</c> writes for a redeclaration, which finds its region
    /// by its site key. Where the region is placed, and what stays where the local is declared, is decided by the shared
    /// memory emitters, not by <see cref="ResumeLocals.CreateInitialization"/>.
    /// </summary>
    Initialized
}
