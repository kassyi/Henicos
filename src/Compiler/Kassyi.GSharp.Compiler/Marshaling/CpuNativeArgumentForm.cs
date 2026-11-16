namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>How the CPU launcher writes one kernel parameter in the call of the native launcher.</summary>
internal enum CpuNativeArgumentForm
{
    /// <summary>The parameter itself: <c>&lt;name&gt;</c>.</summary>
    Value,

    /// <summary>
    /// A proxy built from the pinned data and the length:
    /// <c>new GSharpArrayProxy { Data = (IntPtr)__gsharp_p_&lt;n&gt;, Length = &lt;length&gt; }</c>.
    /// </summary>
    ArrayProxy,

    /// <summary>
    /// The address of the nullable proxy declared before the call: <c>&amp;__gsharp_nullable_&lt;n&gt;</c>.
    /// </summary>
    NullableProxyAddress,

    /// <summary>The address of the native shadow struct: <c>&amp;__gsharp_native_&lt;n&gt;</c>.</summary>
    ShadowAddress,

    /// <summary>The pointer of the <c>fixed</c> statement that pins a <c>ref</c> / <c>out</c> parameter.</summary>
    PinnedAddress,

    /// <summary>The address of a struct value parameter: <c>&amp;&lt;name&gt;</c>.</summary>
    ValueAddress,

    /// <summary>
    /// A copy of the caller's proxy without its reserved flags:
    /// <c>new GSharpArrayProxy { Data = &lt;name&gt;.Data, Length = &lt;name&gt;.Length }</c>.
    /// </summary>
    ProxyCopy,

    /// <summary>The caller's <c>IntPtr</c> cast to the native type: <c>(&lt;type&gt;)&lt;name&gt;</c>.</summary>
    PointerCast,

    /// <summary>A <see langword="bool"/> as a C++ <c>bool</c> byte: <c>&lt;name&gt; ? (byte)1 : (byte)0</c>.</summary>
    BooleanByte,

    /// <summary>A <see langword="char"/> as its UTF-16 code unit: <c>(ushort)&lt;name&gt;</c>.</summary>
    CharCode
}
