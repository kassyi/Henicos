using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Sequencing;

namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>What the plan of C++ names knows of a body while it names the temporaries.</summary>
internal sealed class IrCppNamingState
{
    private readonly Dictionary<IrVariable, int> _sequenceNumbers = new();

    /// <summary>
    /// Numbers the sequence temporaries of <paramref name="root"/> in the order a writer spells them.
    /// </summary>
    public IrCppNamingState(IrNode root)
    {
        var temporaries = IrSequenceNumbering.Of(root);
        for (var index = 0; index < temporaries.Length; index++)
        {
            _sequenceNumbers.Add(temporaries[index], index);
        }
    }

    /// <summary>
    /// The number of <paramref name="variable"/> among the sequence temporaries of the body, counted from 0 in the
    /// order a writer spells their declarations.
    /// </summary>
    public bool TryGetSequenceNumber(IrVariable variable, out int number) =>
        _sequenceNumbers.TryGetValue(variable, out number);
}
