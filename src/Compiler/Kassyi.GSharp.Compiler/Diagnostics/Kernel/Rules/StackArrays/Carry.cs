namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.StackArrays;

/// <summary>How a child expression carries the reference of its parent's value.</summary>
internal enum Carry
{
    /// <summary>The child is the value itself, read the same way as the parent (<c>(a)</c>, <c>(int*)p</c>, <c>h.Field</c>).</summary>
    Through,

    /// <summary>The value of the child flows into the parent's value (an operand, an argument, an element).</summary>
    Value,

    /// <summary>The child is a variable whose address the parent takes (<c>&amp;a[0]</c>, <c>ref a[0]</c>).</summary>
    Address
}
