namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The checks of a call of an array view member before the member acts (<see cref="ArrayViewMethods.GetChecks"/>): the
/// receiver, then the destination of a <c>CopyTo</c>.
/// </summary>
/// <param name="IfDefault">The failure of a receiver without an array.</param>
/// <param name="IfDestinationNull">The failure of a destination without an array.</param>
internal readonly record struct ViewCheckPlan(ViewCheck IfDefault, ViewCheck IfDestinationNull)
{
    /// <summary>A call that checks nothing of its own.</summary>
    public static ViewCheckPlan None => default;

    /// <summary>Whether the call checks anything.</summary>
    public bool HasChecks => IfDefault != ViewCheck.None || IfDestinationNull != ViewCheck.None;
}
