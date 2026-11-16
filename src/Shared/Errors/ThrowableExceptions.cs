#if GSHARP_COMPILER
namespace Kassyi.GSharp.Compiler.Errors;
#else
namespace Kassyi.GSharp.Errors;
#endif

/// <summary>
/// The one table of the exception types that have a <c>GSharpErrorCode</c> of their own, read by the compiler (a
/// <c>throw</c> of the type writes the code) and by the runtime (the code builds the type, with the message text and
/// the parameter name rule). Linked into both projects (netstandard2.0 compatible). Every other exception type is
/// <c>GSharpErrorCode.Throw</c>, which the error site names. <c>DataRace</c> (an <c>InvalidOperationException</c> that
/// no <c>throw</c> writes) is not in the table.
/// </summary>
internal static class ThrowableExceptions
{
    /// <summary>The exception types with a code of their own.</summary>
    public static readonly ThrowableException[] All =
    [
        new("System.NotSupportedException", "NotSupported", "Specified method is not supported.", false),
        new("System.InvalidOperationException", "InvalidOperation",
            "Operation is not valid due to the current state of the object.", false),
        new("System.IndexOutOfRangeException", "IndexOutOfRange", "Index was outside the bounds of the array.", false),
        new("System.ArgumentOutOfRangeException", "ArgumentOutOfRange",
            "Specified argument was out of the range of valid values.", true),
        new("System.ArgumentException", "Argument", "Value does not fall within the expected range.", true),
        new("System.NullReferenceException", "NullReference",
            "Object reference not set to an instance of an object.", false),
        new("System.DivideByZeroException", "DivideByZero", "Attempted to divide by zero.", false),
        new("System.OverflowException", "Overflow", "Arithmetic operation resulted in an overflow.", false),
        new("System.Runtime.CompilerServices.SwitchExpressionException", "SwitchExpression",
            "Non-exhaustive switch expression failed to match its input.", false),
        new("System.ArgumentNullException", "ArgumentNull", "Value cannot be null.", true)
    ];

    /// <summary>Finds the entry of an exception type by its namespace-qualified name.</summary>
    public static bool TryFindByFullName(string fullName, out ThrowableException entry)
    {
        foreach (var candidate in All)
        {
            if (candidate.FullName == fullName)
            {
                entry = candidate;
                return true;
            }
        }

        entry = default;
        return false;
    }

    /// <summary>Finds the entry of a <c>GSharpErrorCode</c> member by its name.</summary>
    public static bool TryFindByCodeName(string codeName, out ThrowableException entry)
    {
        foreach (var candidate in All)
        {
            if (candidate.CodeName == codeName)
            {
                entry = candidate;
                return true;
            }
        }

        entry = default;
        return false;
    }
}
