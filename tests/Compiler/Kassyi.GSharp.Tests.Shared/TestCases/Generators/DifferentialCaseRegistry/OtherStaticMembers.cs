using System.Collections.Generic;
using Kassyi.GSharp.Tests.Shared.Differential;

namespace Holders
{
    // Static members of other types, instance members and methods: none of them is a case member
    public class OtherMembers
    {
        internal static readonly string Name = "OtherMembers";
        internal static readonly object Boxed = new DifferentialCase("OtherMembers.Boxed");
        internal static readonly Other.DifferentialCase SameSimpleName = new("OtherMembers.SameSimpleName");
        internal static readonly List<string> Names = ["OtherMembers"];
        internal static int Count { get; } = 1;
        internal readonly DifferentialCase Instance = new("OtherMembers.Instance");
        internal DifferentialCase InstanceProperty { get; } = new("OtherMembers.InstanceProperty");
        internal static DifferentialCase Create() => new("OtherMembers.Create");
        internal const string Constant = "OtherMembers";
    }
}
