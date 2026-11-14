namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The messages of the argument exceptions that device code raises for a failed check of a BCL call, worded as .NET
/// words them. The text after the last <c>" (Parameter '"</c> names the parameter: <c>GSharpErrorMapper</c> reads it
/// into <see cref="ArgumentException.ParamName"/>, since the device error record has no field for it.
/// </summary>
/// <remarks>
/// A kernel registers each message as a site of its error site table, which adds the place. A device function has no
/// table, so its check passes the text itself, and <c>GSharpErrorMapper</c> knows it as a builtin message (the list
/// there repeats these texts; a test keeps the two equal).
/// </remarks>
internal static class ArgumentExceptionMessages
{
    /// <summary><c>Array.Fill(array, value, startIndex, count)</c> with a start outside the array.</summary>
    public const string FillStartIndex =
        "Index was out of range. Must be non-negative and less than or equal to the size of the collection. " +
        "(Parameter 'startIndex')";

    /// <summary><c>Array.Fill(array, value, startIndex, count)</c> with a count past the end.</summary>
    public const string FillCount =
        "Count must be positive and count must refer to a location within the string/array/collection. " +
        "(Parameter 'count')";

    /// <summary><c>Array.Copy</c> from an array that is too short for the range.</summary>
    public const string CopySource =
        "Source array was not long enough. Check the source index, length, and the array's lower bounds. " +
        "(Parameter 'sourceArray')";

    /// <summary><c>Array.Copy</c> into an array that is too short for the range.</summary>
    public const string CopyDestination =
        "Destination array was not long enough. Check the destination index, length, and the array's lower bounds. " +
        "(Parameter 'destinationArray')";

    /// <summary><c>ElementAt(index)</c> with an index outside the sequence.</summary>
    public const string ElementAtIndex =
        "Index was out of range. Must be non-negative and less than the size of the collection. " +
        "(Parameter 'index')";

    /// <summary>Every message above, for the builtin list of the runtime.</summary>
    public static readonly string[] All = [FillStartIndex, FillCount, CopySource, CopyDestination, ElementAtIndex];
}
