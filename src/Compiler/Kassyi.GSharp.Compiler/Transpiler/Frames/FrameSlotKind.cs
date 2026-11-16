namespace Kassyi.GSharp.Compiler.Transpiler.Frames;

/// <summary>Where a hoisted local of a CPU state machine is kept between two resumes.</summary>
internal enum FrameSlotKind
{
    /// <summary>A field of the frame.</summary>
    Field,

    /// <summary>
    /// Not in the frame: declared again before the state switch on every resume, with a default value.
    /// </summary>
    Redeclared,

    /// <summary>
    /// Not in the frame: a stackalloc of a constant length, whose memory is declared again before the state switch
    /// (<see cref="FrameSlot.ElementType"/>, <see cref="FrameSlot.Length"/>).
    /// </summary>
    RedeclaredStack,

    /// <summary>
    /// A <c>SharedMemory&lt;T&gt;</c>: a field of the native frame, where the launcher places it; declared again on
    /// every resume in managed code, which finds its region by its site key.
    /// </summary>
    SharedMemory,

    /// <summary>A stackalloc whose length is not a constant: reported (GSH0003) by the native writer.</summary>
    Unsupported,

    /// <summary>
    /// A <c>ref</c> local: a field of the native frame (which holds a copy of the target); no member of the managed
    /// frame, whose state machine only throws.
    /// </summary>
    HeldRef
}
