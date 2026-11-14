namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The messages of the argument exceptions that device code raises for a failed check of a BCL call, worded as
/// .NET 10 words them (without the actual value that its argument-out-of-range messages append). The text after the
/// last <c>" (Parameter '"</c> names the parameter: <c>GSharpErrorMapper</c> reads it into
/// <see cref="ArgumentException.ParamName"/>, since the device error record has no field for it.
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

    /// <summary><c>Array.Copy</c> with a negative length.</summary>
    public const string CopyLength = "length must be a non-negative value. (Parameter 'length')";

    /// <summary><c>Array.Copy</c> with a negative source index.</summary>
    public const string CopySourceIndex =
        "sourceIndex must be greater than or equal to '0'. (Parameter 'sourceIndex')";

    /// <summary><c>Array.Copy</c> with a negative destination index.</summary>
    public const string CopyDestinationIndex =
        "destinationIndex must be greater than or equal to '0'. (Parameter 'destinationIndex')";

    /// <summary><c>ElementAt(index)</c> with an index outside the sequence.</summary>
    public const string ElementAtIndex =
        "Index was out of range. Must be non-negative and less than the size of the collection. " +
        "(Parameter 'index')";

    /// <summary><c>Span.CopyTo</c> into a shorter span (written by the prelude, not by an emitter).</summary>
    public const string SpanCopyToDestination = "Destination is too short. (Parameter 'destination')";

    /// <summary>Every message above, for the builtin list of the runtime.</summary>
    public static readonly string[] All = [
        FillStartIndex, FillCount, CopySource, CopyDestination, CopyLength, CopySourceIndex,
        CopyDestinationIndex, ElementAtIndex,
        SpanCopyToDestination,
    ];
}
