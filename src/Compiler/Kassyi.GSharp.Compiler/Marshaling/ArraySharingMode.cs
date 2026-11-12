namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>How a launcher finds the parameters of one launch that hold the same array.</summary>
internal enum ArraySharingMode
{
    /// <summary>
    /// No two holders of one array are possible (at most one <c>T[]</c> argument and no struct or element that holds
    /// arrays), or the launcher copies nothing (the <c>IntPtr</c> wrapper).
    /// </summary>
    None,

    /// <summary>
    /// Only <c>T[]</c> arguments can hold one array: the launcher compares each with the holders before it
    /// (<see cref="ArraySharingPlan.ReferenceEqualsMethod"/>), with no allocation. The CPU launcher always works this way.
    /// </summary>
    Pairwise,

    /// <summary>
    /// A struct argument or a converted element holds arrays: on CUDA every holder looks its array up in the launch's
    /// <c>DeviceArrayRegions</c>, which the generated <c>Marshal_</c> methods also use.
    /// </summary>
    Table
}
