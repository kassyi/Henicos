namespace Kassyi.GSharp.Compiler.Tasks.Helpers;

/// <summary>One row of a process table: the process, the process that created it, and when it was created.</summary>
/// <param name="ProcessId">The process ID.</param>
/// <param name="ParentProcessId">The ID of the process that created it, as the OS recorded it at creation.</param>
/// <param name="StartTime">
/// The creation time in the OS's own unit, or <see langword="null"/> when the table has none.
/// </param>
internal readonly record struct ProcessEntry(int ProcessId, int ParentProcessId, long? StartTime);
