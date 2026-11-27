using Kassyi.GSharp.Compiler.Ir;

namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>Gives a C++ name to one kind of IR temporary.</summary>
internal interface IIrCppTemporaryNamer
{
    /// <summary>
    /// Sets <paramref name="cpp"/> to the C++ name of <paramref name="variable"/> when this namer handles its kind.
    /// </summary>
    bool TryName(IrVariable variable, IrCppNamingState state, out string cpp);
}
