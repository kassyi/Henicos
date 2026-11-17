namespace Kassyi.GSharp.Compiler.ErrorSites;

/// <summary>What the error site of a <c>throw</c> carries besides its place.</summary>
/// <param name="ExceptionType">
/// The assembly-qualified CLR name of the exception type for <c>GSharpErrorCode.Throw</c>, or empty when the code names
/// the type.
/// </param>
/// <param name="Message">The constant message of the exception, with the <c> (Parameter 'x')</c> suffix when it has a parameter name, or empty.</param>
internal readonly record struct ThrownException(string ExceptionType, string Message)
{
    /// <summary>The trap kind of the site, which keeps its ID apart from the other checks at the same place.</summary>
    public const string TrapKind = "Throw";
}
