namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>The temporary namers of the C++ writer, in the order <see cref="IrCppNames"/> tries them.</summary>
internal static class IrCppTemporaryNamers
{
    /// <summary>The namers of the C++ writer.</summary>
    public static readonly IIrCppTemporaryNamer[] Namers = [new IrCppSequenceTemporaryNamer()];
}
