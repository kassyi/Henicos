namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>One kernel parameter in the call of the CPU native launcher.</summary>
/// <param name="TypeName">The C# type of the parameter of the native launcher, a pointer type included.</param>
/// <param name="Form">How the CPU launcher writes the argument.</param>
internal readonly record struct CpuNativeArgument(string TypeName, CpuNativeArgumentForm Form);
