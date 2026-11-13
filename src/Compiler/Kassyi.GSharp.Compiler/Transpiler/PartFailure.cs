namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Whether a part met a construct that could not be emitted, set by an intercepting reporter.</summary>
internal sealed class PartFailure
{
    /// <summary>Whether a failure was reported.</summary>
    public bool Failed { get; private set; }

    /// <summary>Records a failure.</summary>
    public void Mark() => Failed = true;

    /// <summary>The message of an exception that a record keeps (its type and message, without the stack).</summary>
    public static string Describe(Exception exception) => exception.GetType().Name + ": " + exception.Message;
}
