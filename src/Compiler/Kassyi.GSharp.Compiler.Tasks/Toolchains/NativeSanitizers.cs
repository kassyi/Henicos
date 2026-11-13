namespace Kassyi.GSharp.Compiler.Tasks.Toolchains;

/// <summary>
/// Specifies the native C++ sanitizers to enable when compiling host shadow kernels.
/// </summary>
[Flags]
public enum NativeSanitizers
{
    /// <summary>No native sanitizers enabled.</summary>
    None = 0,

    /// <summary>AddressSanitizer (<c>-fsanitize=address</c> on GCC/Clang, <c>/fsanitize=address</c> on MSVC).</summary>
    Address = 1 << 0,

    /// <summary>UndefinedBehaviorSanitizer (<c>-fsanitize=undefined</c> on GCC/Clang).</summary>
    Undefined = 1 << 1
}
