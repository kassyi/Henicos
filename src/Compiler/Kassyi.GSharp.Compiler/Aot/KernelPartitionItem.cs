namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>
/// A kernel item for partition planning.
/// </summary>
/// <param name="ClassFullName">The namespace-qualified unique type name of the kernel class.</param>
/// <param name="ByteCount">The UTF-8 byte count of the kernel's emitted native body.</param>
internal readonly record struct KernelPartitionItem(string ClassFullName, int ByteCount);
