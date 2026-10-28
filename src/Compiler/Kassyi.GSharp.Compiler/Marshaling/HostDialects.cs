namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// The two dialects whose launchers the host model describes, one instance each for the host model, the parameter
/// shapes and the struct marshaling.
/// </summary>
/// <remarks>A dialect holds no per-compilation state, so one shared instance serves every kernel.</remarks>
internal static class HostDialects
{
    /// <summary>The CUDA dialect.</summary>
    public static readonly ITranspilerDialect Cuda = new GSharpCudaDialect();

    /// <summary>The CPU (standard C++) dialect.</summary>
    public static readonly ITranspilerDialect Cpu = new StandardCppDialect();
}
