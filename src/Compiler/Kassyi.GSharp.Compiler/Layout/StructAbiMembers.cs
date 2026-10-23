namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// The members that make up the native layout of a user struct. The device definition (<c>StructTranspiler</c>), the
/// host shadow struct (<c>NativeStructEmitter</c>), its marshaling (<c>StructMarshalingEmitter</c>) and the layout
/// computation (<see cref="CudaLayoutRules"/>) walk this one set so that both sides agree on the offsets.
/// </summary>
/// <remarks>
/// The device definition does not use this set yet: it declares the non-static members as written (an
/// auto-property under its own name, an <c>[InlineArray(N)]</c> struct as <c>T element[N]</c>, a <c>fixed</c> buffer as
/// <c>T name[N]</c>). The host differs from it in these places (#468, pinned by <c>StructAbiTests</c>):
/// <list type="bullet">
///   <item><description>The shadow offsets ignore <c>[StructLayout(Pack = n)]</c>, which the size honours.</description></item>
///   <item><description>An <c>[InlineArray(N)]</c> field is sized as one element, and a private element field does not compile in the marshaling code.</description></item>
///   <item><description>A <c>fixed</c> buffer is sized as the pointer Roslyn types it with, and its marshaling does not compile.</description></item>
///   <item><description>An auto-property appears as its backing field, whose name does not compile in the shadow struct.</description></item>
/// </list>
/// </remarks>
internal static class StructAbiMembers
{
    /// <summary>The instance fields of <paramref name="structType"/> in declaration order, backing fields included.</summary>
    public static InstanceFields GetInstanceFields(INamedTypeSymbol structType) => new(structType.GetMembers());

    /// <summary>A <c>foreach</c>-able view of the instance fields that allocates nothing.</summary>
    internal readonly struct InstanceFields(ImmutableArray<ISymbol> members)
    {
        public InstanceFieldEnumerator GetEnumerator() => new(members);
    }

    internal struct InstanceFieldEnumerator(ImmutableArray<ISymbol> members)
    {
        private int _index = -1;

        public readonly IFieldSymbol Current => (IFieldSymbol)members[_index];

        public bool MoveNext()
        {
            while (++_index < members.Length)
            {
                if (members[_index] is IFieldSymbol { IsStatic: false })
                {
                    return true;
                }
            }

            return false;
        }
    }
}
