using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Transpiler;

// Every member body that creates one SharedMemory<int>; the count argument (1 to 6) names the creation
public struct SiteKeyOwners
{
    public int GetterAndSetter
    {
        get
        {
            var s = new SharedMemory<int>(1);
            return s[0];
        }
        set
        {
            var s = new SharedMemory<int>(2);
            s[0] = value;
        }
    }

    public int GetterAndInit
    {
        get
        {
            var s = new SharedMemory<int>(3);
            return s[0];
        }
        init
        {
            var s = new SharedMemory<int>(4);
            s[0] = value;
        }
    }

    public int ExpressionBodied => new SharedMemory<int>(5)[0];

    public int Method()
    {
        var s = new SharedMemory<int>(6);
        return s[0];
    }
}
