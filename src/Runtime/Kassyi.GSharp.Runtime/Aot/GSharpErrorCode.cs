namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Represents the error code recorded by the G# device or native CPU runtime error channel.
/// </summary>
public enum GSharpErrorCode
{
    /// <summary>No error occurred.</summary>
    None = 0,

    /// <summary>Corresponds to <see cref="NotSupportedException"/>.</summary>
    NotSupported = 1,

    /// <summary>Corresponds to <see cref="InvalidOperationException"/>.</summary>
    InvalidOperation = 2,

    /// <summary>Corresponds to <see cref="IndexOutOfRangeException"/>.</summary>
    IndexOutOfRange = 3,

    /// <summary>Corresponds to <see cref="ArgumentOutOfRangeException"/>.</summary>
    ArgumentOutOfRange = 4,

    /// <summary>Corresponds to <see cref="ArgumentException"/>.</summary>
    Argument = 5,

    /// <summary>Corresponds to <see cref="NullReferenceException"/>.</summary>
    NullReference = 6,

    /// <summary>Corresponds to <see cref="DivideByZeroException"/>.</summary>
    DivideByZero = 7,

    /// <summary>Corresponds to <see cref="OverflowException"/>.</summary>
    Overflow = 8,

    /// <summary>Corresponds to <see cref="InvalidOperationException"/> for detected shared-memory data races.</summary>
    DataRace = 9,

    /// <summary>
    /// Corresponds to <see cref="System.Runtime.CompilerServices.SwitchExpressionException"/>: no arm of a switch
    /// expression matched its input.
    /// </summary>
    SwitchExpression = 10,

    /// <summary>
    /// Corresponds to <see cref="ArgumentNullException"/>: a null destination array of <c>CopyTo</c> on an
    /// <c>ArraySegment&lt;T&gt;</c> or <c>ImmutableArray&lt;T&gt;</c>.
    /// </summary>
    ArgumentNull = 11
}
