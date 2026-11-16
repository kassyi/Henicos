namespace Kassyi.GSharp.Compiler.Models;

/// <summary>
/// The managed forms that a library emits for one exported device function, recorded in its
/// <see cref="DeviceExportRecord.FunctionTraits"/>.
/// </summary>
/// <param name="StateMachine">The library holds the managed state machine of the suspending function.</param>
/// <param name="PrivateFrame">
/// The frame of that state machine is private to the declaring type; callers run it through its entries.
/// </param>
/// <param name="Version">The library holds the managed version of a function that does not suspend.</param>
internal readonly record struct ExportedManagedForms(bool StateMachine, bool PrivateFrame, bool Version);
