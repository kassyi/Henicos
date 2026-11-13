namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>What an accessor member function of a struct property runs.</summary>
internal enum PropertyAccessorBody : byte
{
    /// <summary>No member function: the property has no such accessor, or its body is not in this
    /// compilation.</summary>
    None,

    /// <summary>The body written in C# (<c>get =&gt; ...</c>, <c>set { ... }</c>, or the property's <c>=&gt;
    /// ...</c>).</summary>
    Written,

    /// <summary>The body the C# compiler implements for <c>get;</c>: <c>return &lt;backing field&gt;;</c>.</summary>
    ReadsBackingField,

    /// <summary>The body the C# compiler implements for <c>set;</c> / <c>init;</c>: <c>&lt;backing field&gt; =
    /// value;</c>.</summary>
    WritesBackingField
}
