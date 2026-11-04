namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// A public launch option parameter appended to generated kernel wrappers.
/// </summary>
internal readonly record struct LaunchOption(string Type, string Name, string DefaultValue);
