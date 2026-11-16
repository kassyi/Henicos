namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;

/// <summary>How the receiver of a <c>GetPinnableReference</c> that a <c>fixed</c> statement calls is passed.</summary>
internal enum PinnableReceiverForm
{
    /// <summary>By value: the extension takes its receiver as an ordinary parameter.</summary>
    Value,

    /// <summary>By the address of the variable that the receiver names (<c>ref this</c>, <c>in this</c>).</summary>
    Address,

    /// <summary>
    /// By the address of a copy that a local declared before the declarator holds, for a value that no variable
    /// holds (<c>fixed (int* p = Make())</c> with <c>in this</c>): C# keeps the copy for the whole <c>fixed</c> body,
    /// and a C++ temporary would die at the end of the declaration.
    /// </summary>
    Spill
}
