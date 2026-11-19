namespace Kassyi.GSharp.Cuda;

/// <summary>
/// The oldest CUDA Toolkit release that G# builds with, shared between the MSBuild tasks (which check
/// <c>nvcc --version</c>) and the prelude generator (which emits a <c>__CUDACC_VER_MAJOR__</c> guard).
/// </summary>
/// <remarks>
/// Matches the CUDA Toolkit requirement documented in the Build item of <c>README.md</c> (Requirements) and
/// <c>docs/spec/en/00_getting_started/01_prerequisites.md</c> (Build Tools).
/// </remarks>
internal static class NvccMinimum
{
    /// <summary>The oldest CUDA Toolkit major release that G# builds with.</summary>
    public const int MajorRelease = 13;
}
