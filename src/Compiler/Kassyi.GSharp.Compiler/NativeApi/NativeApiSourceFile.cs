namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>The path and text of a native API definition file (<c>GSharpApi="true"</c>).</summary>
internal readonly record struct NativeApiSourceFile(string Path, string Text);
