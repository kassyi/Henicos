using Kassyi.GSharp.Compiler.Ir;

namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// Names the temporaries of the sequence pass as the legacy route names its order temporaries: the prefix of
/// <see cref="GSharpInternalNames.Lowering.SequenceTempPrefix"/> and the number of the temporary in the body.
/// </summary>
/// <remarks>
/// The names are the plan a writer checks its first spelling against: a writer that spells a target twice takes new
/// numbers from the counter of the method for the second spelling.
/// </remarks>
internal sealed class IrCppSequenceTemporaryNamer : IIrCppTemporaryNamer
{
    /// <inheritdoc/>
    public bool TryName(IrVariable variable, IrCppNamingState state, out string cpp)
    {
        if (!state.TryGetSequenceNumber(variable, out var number))
        {
            cpp = "";
            return false;
        }

        cpp = GSharpInternalNames.Lowering.SequenceTempPrefix + number.ToString(CultureInfo.InvariantCulture);
        return true;
    }
}
