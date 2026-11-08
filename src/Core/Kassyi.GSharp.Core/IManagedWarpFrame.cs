using System.ComponentModel;

namespace Kassyi.GSharp.Core;

/// <summary>
/// The frame of a managed state machine that reaches a warp collective: its number among the frames of the thread and
/// how often it reached each warp collective of its body. Generated code implements it. Do not use directly.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IManagedWarpFrame
{
    /// <summary>The number of the frame among the frames of the thread, the same in every thread.</summary>
    uint WarpPath { get; }

    /// <summary>Counts one more visit of a warp collective of the body and returns the count.</summary>
    /// <param name="site">The number of the warp collective in the body.</param>
    /// <returns>How often the frame has reached the warp collective, this visit included.</returns>
    uint AdvanceWarpVisit(int site);

    /// <summary>Returns how often the frame has reached a warp collective of the body.</summary>
    /// <param name="site">The number of the warp collective in the body.</param>
    /// <returns>The visit count.</returns>
    uint GetWarpVisit(int site);
}
