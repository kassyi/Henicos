#if GSHARP_COMPILER
namespace Kassyi.GSharp.Compiler.Errors;
#else
namespace Kassyi.GSharp.Errors;
#endif

/// <summary>
/// An exception type that a device error code stands for: the compiler maps a <c>throw</c> of <see cref="FullName"/> to
/// <see cref="CodeName"/>, and the runtime builds that type from the code.
/// </summary>
/// <param name="FullName">The namespace-qualified name of the exception type.</param>
/// <param name="CodeName">The name of the <c>GSharpErrorCode</c> member.</param>
/// <param name="DefaultMessage">The message of the parameterless constructor, which the exception has without a message.</param>
/// <param name="HasParameterName">Whether the type carries a parameter name (<c>ParamName</c>), which the error message holds as the suffix <c> (Parameter 'x')</c>.</param>
internal readonly record struct ThrowableException(
    string FullName,
    string CodeName,
    string DefaultMessage,
    bool HasParameterName);
