using Kassyi.GSharp.Compiler.Analysis;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>
/// What the classify pass decided about a call: whether it splits the state machine of the CPU paths and what it may
/// write. The builder leaves every call <see cref="Unclassified"/>.
/// </summary>
/// <remarks>
/// [WHY] Unclassified is a state of its own, not <see cref="IrSplit.None"/> with
/// <see cref="DeviceFunctionEffect.None"/>:
/// a pass that reads a call the classify pass has not seen fails instead of taking "no split, writes nothing".
/// <see langword="default"/> is unclassified.
/// </remarks>
internal readonly struct IrCallClassification : IEquatable<IrCallClassification>
{
    private readonly IrSplit _split;
    private readonly DeviceFunctionEffect _effect;

    private IrCallClassification(IrSplit split, DeviceFunctionEffect effect)
    {
        _split = split;
        _effect = effect;
        IsClassified = true;
    }

    /// <summary>The state of a call the classify pass has not seen.</summary>
    public static IrCallClassification Unclassified => default;

    /// <summary>Whether the classify pass has decided the call.</summary>
    public bool IsClassified { get; }

    /// <summary>Whether and how the call splits the state machine.</summary>
    /// <exception cref="InvalidOperationException">The call is not classified.</exception>
    public IrSplit Split => IsClassified ? _split : throw NotClassified();

    /// <summary>What the call may write besides its own locals.</summary>
    /// <exception cref="InvalidOperationException">The call is not classified.</exception>
    public DeviceFunctionEffect Effect => IsClassified ? _effect : throw NotClassified();

    /// <summary>A decided call.</summary>
    public static IrCallClassification Of(IrSplit split, DeviceFunctionEffect effect) => new(split, effect);

    /// <summary>Whether both are unclassified, or both decided the same.</summary>
    public bool Equals(IrCallClassification other) =>
        IsClassified == other.IsClassified && _split == other._split && _effect == other._effect;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is IrCallClassification other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => IsClassified ? (_split.GetHashCode() * 31) + (int)_effect : 0;

    private static InvalidOperationException NotClassified() =>
        new("The call has not been classified; run the classify pass first.");
}
