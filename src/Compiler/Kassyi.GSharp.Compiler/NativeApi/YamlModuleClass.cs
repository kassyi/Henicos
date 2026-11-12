namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// A class that a YAML <c>moduleName</c> binding extends, and the module whose field
/// (<see cref="ModuleKernelLaunchEmitter.GetModuleFieldName"/>) the binding declares in it. <c>[CudaImport]</c>
/// methods of the same module in that class use that field instead of declaring a second one.
/// </summary>
/// <param name="TypeName">
/// The metadata name of the class: <c>&lt;namespace&gt;.&lt;class&gt;</c>, keywords not escaped.
/// </param>
/// <param name="ModuleName">The module name as the binding writes it.</param>
internal readonly record struct YamlModuleClass(string TypeName, string ModuleName);
