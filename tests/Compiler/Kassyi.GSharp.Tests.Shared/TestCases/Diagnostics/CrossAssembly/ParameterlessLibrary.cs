using Kassyi.GSharp.Core;

// A library: a struct whose declared parameterless constructor has a body (#609)
namespace XasmLib
{
    public struct Counter
    {
        public int Value;

        public Counter()
        {
            Value = 1;
        }
    }
}
