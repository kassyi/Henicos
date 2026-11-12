namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>One member of a struct with its byte offset, member layout, and whether it is the copied arm of a union offset group.</summary>
public readonly record struct FieldLayout(StructAbiMember Member, int Offset, TypeLayout Layout, bool IsCopied = true)
{
    /// <summary>Deconstructs into member, offset, and layout without the copy flag.</summary>
    public void Deconstruct(out StructAbiMember member, out int offset, out TypeLayout layout)
    {
        member = Member;
        offset = Offset;
        layout = Layout;
    }
}
