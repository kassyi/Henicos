namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>Why a <c>[GpuKernel]</c> declaration cannot have a launcher (GSH0111).</summary>
internal enum KernelPlacement
{
    /// <summary>An ordinary static method of a class: the generator gives it a launcher.</summary>
    Allowed,
    Lambda,
    LocalFunction,
    Operator,
    ConversionOperator,
    Accessor,
    StructMember,
    InterfaceMember,

    /// <summary>A member of a type that is not a class, a struct or an interface: a C# 14 extension block.</summary>
    ExtensionBlockMember,
    InstanceMethod,
    GenericMethod,
    MemberOfGenericType,
    EntryPoint,
    ModuleInitializer,
    UnmanagedCallersOnly,
    InterfaceImplementation,

    /// <summary>
    /// An extension method that C# calls by its name to expand a statement or expression (<c>foreach</c>, ...).
    /// </summary>
    PatternExtension,
    CollectionBuilderMethod
}
