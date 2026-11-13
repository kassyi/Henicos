namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>One kernel parameter in the call of the CPU native launcher.</summary>
/// <param name="TypeName">The C# type of the parameter of the native launcher, a pointer type included.</param>
/// <param name="Form">How the CPU launcher writes the argument.</param>
/// <param name="Group">
/// For a converted member of a group of overlapping arguments, the test that the argument is a member and the address
/// of its elements in the group's buffer, which the native kernel takes instead of the member's own shadow;
/// otherwise <see langword="null"/>.
/// </param>
internal readonly record struct CpuNativeArgument(
    string TypeName,
    CpuNativeArgumentForm Form,
    (string Member, string Pointer)? Group = null
);
