namespace Kassyi.GSharp.Compiler.Tasks.NativeUnits;

/// <summary>The translation units that are linked into one native module.</summary>
/// <param name="ModuleStem">The module name that the linked library is named after.</param>
/// <param name="Units">The units of the module, in source order.</param>
internal readonly record struct NativeModuleGroup(string ModuleStem, NativeUnitSource[] Units);
