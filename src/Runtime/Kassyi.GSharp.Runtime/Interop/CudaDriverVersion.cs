namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The oldest NVIDIA driver the runtime runs on.
/// </summary>
/// <remarks>
/// From the "CUDA Driver" section of the CUDA 13.0 release notes
/// (https://docs.nvidia.com/cuda/archive/13.0.0/cuda-toolkit-release-notes/index.html#cuda-driver): CUDA 13.0 GA needs
/// 580.65.06 or newer on Linux x86_64 and lists no Windows version (the Windows driver is no longer bundled); CUDA 13.x
/// minor version compatibility needs a driver of branch R580 or newer.
/// </remarks>
internal static class CudaDriverVersion
{
    /// <summary>
    /// The oldest CUDA version the driver must support, in the encoding of <c>cuDriverGetVersion</c>
    /// (<c>1000 * major + 10 * minor</c>): CUDA 13.0.
    /// </summary>
    public const int Minimum = 13000;

    /// <summary>The driver branch of <see cref="Minimum"/>.</summary>
    public const string MinimumDriverBranch = "R580";

    /// <summary>The oldest Linux driver of <see cref="Minimum"/>.</summary>
    public const string MinimumLinuxDriver = "580.65.06";

    /// <summary>Gets whether a driver that reports <paramref name="reportedVersion"/> is supported.</summary>
    /// <param name="reportedVersion">The version <c>cuDriverGetVersion</c> returned.</param>
    /// <returns><see langword="true"/> when the version is <see cref="Minimum"/> or newer.</returns>
    public static bool IsSupported(int reportedVersion) => reportedVersion >= Minimum;

    /// <summary>Describes why a driver that reports <paramref name="reportedVersion"/> is not supported.</summary>
    /// <param name="reportedVersion">The version <c>cuDriverGetVersion</c> returned.</param>
    /// <returns>The message naming the version found and the version required.</returns>
    public static string DescribeUnsupported(int reportedVersion) =>
        $"The installed NVIDIA driver supports CUDA {Format(reportedVersion)}, but G# requires a driver for "
        + $"CUDA {Format(Minimum)} or newer (driver branch {MinimumDriverBranch} or newer; {MinimumLinuxDriver} or "
        + "newer on Linux). Update the NVIDIA driver.";

    /// <summary>Formats a <c>cuDriverGetVersion</c> value as <c>major.minor</c>.</summary>
    /// <param name="version">The version, <c>1000 * major + 10 * minor</c>.</param>
    /// <returns>The version as <c>major.minor</c>, for example <c>12.8</c> for 12080.</returns>
    public static string Format(int version) => $"{version / 1000}.{version % 1000 / 10}";
}
