using Kassyi.GSharp.Compiler.Ir;

namespace Kassyi.GSharp.Compiler.Backends.Cpp.Support;

/// <summary>
/// Whether a family of C++ writer support rules covers <paramref name="node"/> on the route of
/// <paramref name="profile"/>; when it does, <paramref name="support"/> says whether it is spelled and its shape.
/// </summary>
internal delegate bool SupportGroup(IrNode node, IrNode? parent, CppBodyProfile profile, out IrSupport support);
