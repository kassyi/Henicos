namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>A <see cref="CudaDriverState"/> with the reason the driver is not usable.</summary>
/// <param name="State">The state.</param>
/// <param name="Reason">Why the driver is not usable; <see langword="null"/> when <paramref name="State"/> is
/// <see cref="CudaDriverState.Usable"/>.</param>
internal readonly record struct CudaDriverClassification(CudaDriverState State, string? Reason)
{
    /// <summary>The usable driver.</summary>
    public static CudaDriverClassification Usable => new(CudaDriverState.Usable, null);
}
