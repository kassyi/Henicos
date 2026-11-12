namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>Whether a generated launcher can name a member to check its offset.</summary>
internal enum MemberAccess
{
    /// <summary>Not an ordinary field the launcher can name: private, a pointer, a property, an inline array element.</summary>
    NotNameable,

    /// <summary>A public field.</summary>
    Public,

    /// <summary>An internal field, nameable only from its own assembly (<see cref="MemberPlan.AccessAssembly"/>).</summary>
    Internal
}
