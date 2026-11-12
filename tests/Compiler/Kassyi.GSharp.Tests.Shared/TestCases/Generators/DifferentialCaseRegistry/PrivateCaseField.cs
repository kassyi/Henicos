using Kassyi.GSharp.Tests.Shared.Differential;

namespace Holders
{
    public class PrivateHolder
    {
        // The registry cannot reference it: a compile error in the generated code, not a case left out
        private static readonly DifferentialCase Hidden = new("PrivateHolder.Hidden");
    }
}
