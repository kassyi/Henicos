using System.Collections.Generic;
using Kassyi.GSharp.Tests.Shared.Differential;

namespace Holders
{
    public class FieldHolder
    {
        internal static readonly DifferentialCase Single = new("FieldHolder.Single");
        internal static readonly DifferentialCase First = new("FieldHolder.First"), Second = new("FieldHolder.Second");
        public static DifferentialCase Mutable = new("FieldHolder.Mutable");
    }

    internal static class PropertyHolder
    {
        public static DifferentialCase AutoProperty { get; } = new("PropertyHolder.AutoProperty");
        internal static DifferentialCase ExpressionProperty => new("PropertyHolder.ExpressionProperty");
    }

    public class CollectionHolder
    {
        internal static readonly DifferentialCase[] Array = [new("CollectionHolder.Array")];
        internal static readonly List<DifferentialCase> List = [new("CollectionHolder.List")];
        internal static IReadOnlyList<DifferentialCase> ReadOnlyList { get; } = [new("CollectionHolder.ReadOnlyList")];
    }

    public class Outer
    {
        internal static class Nested
        {
            internal static readonly DifferentialCase Inner = new("Outer.Nested.Inner");
        }
    }
}
