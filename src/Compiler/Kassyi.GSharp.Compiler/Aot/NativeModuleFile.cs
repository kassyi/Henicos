namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>One native module file planned for emission.</summary>
internal readonly record struct NativeModuleFile(string FileName, string BodySource, bool IncludeErrorSites);
