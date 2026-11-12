using Kassyi.GSharp.Compiler.Helpers;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>The text of one struct for one dialect, as its record stores it.</summary>
/// <param name="Definition">The definitions of the unrecorded structs it names, then its own definition.</param>
/// <param name="Dispose">Its <c>Dispose</c> definition, or an empty string.</param>
/// <param name="Failed">Whether the dialect could not transpile it.</param>
/// <param name="Includes">The <c>#include</c> targets of the text.</param>
/// <param name="KeptNames">The C++ names of the unrecorded structs that <paramref name="Definition"/> defines.</param>
/// <param name="InternalError">The message of an exception that stopped writing it, or an empty string.</param>
internal readonly record struct StructDialectText(
    string Definition,
    string Dispose,
    bool Failed,
    EquatableArray<string> Includes,
    EquatableArray<string> KeptNames,
    string InternalError);
