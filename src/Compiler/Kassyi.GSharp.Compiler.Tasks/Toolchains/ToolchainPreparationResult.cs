namespace Kassyi.GSharp.Compiler.Tasks.Toolchains;

/// <summary>
/// Represents the result of preparing a native C++ toolchain environment before compilation.
/// </summary>
public enum ToolchainPreparationResult
{
    /// <summary>The toolchain is available and ready for compilation.</summary>
    Ready,

    /// <summary>The toolchain is unavailable, and compilation should be skipped gracefully with a warning.</summary>
    SkipCompilation,

    /// <summary>The toolchain failed to initialize, and the build should fail with an error.</summary>
    Failed
}
