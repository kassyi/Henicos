namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The oldest NVIDIA driver the runtime runs on, and the check made when the runtime initializes CUDA.
/// </summary>
internal static class CudaDriverVersion
{
    /// <summary>
    /// The oldest CUDA version the driver must support, in the encoding of <c>cuDriverGetVersion</c>
    /// (<c>1000 * major + 10 * minor</c>): CUDA 13.0, driver branch R580.
    /// </summary>
    public const int Minimum = 13000;

    /// <summary>
    /// Throws when the driver supports a CUDA version older than <see cref="Minimum"/>.
    /// </summary>
    /// <param name="reportedVersion">The version <c>cuDriverGetVersion</c> returned.</param>
    /// <exception cref="NotSupportedException">The driver is older than <see cref="Minimum"/>.</exception>
    public static void ThrowIfUnsupported(int reportedVersion)
    {
        if (reportedVersion >= Minimum)
        {
            return;
        }

        throw new NotSupportedException(
            $"The installed NVIDIA driver supports CUDA {Format(reportedVersion)}, but G# requires a driver for "
            + $"CUDA {Format(Minimum)} or newer (driver branch R580 or newer; 580.65.06 or newer on Linux). "
            + "Update the NVIDIA driver."
        );
    }

    /// <summary>Formats a <c>cuDriverGetVersion</c> value as <c>major.minor</c>.</summary>
    /// <param name="version">The version, <c>1000 * major + 10 * minor</c>.</param>
    /// <returns>The version as <c>major.minor</c>, for example <c>12.8</c> for 12080.</returns>
    public static string Format(int version) => $"{version / 1000}.{version % 1000 / 10}";
}
