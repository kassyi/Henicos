using Kassyi.GSharp.Core;

namespace XasmLib
{
    public struct Meters
    {
        public double V;

        public static explicit operator Meters(double d) => new Meters { V = d };
        public static implicit operator Meters(int i) => new Meters { V = i };
        public static implicit operator double(Meters m) => m.V;
    }

    public struct Box<T>
    {
        public T Value;

        public static explicit operator Box<T>(T value) => new Box<T> { Value = value };
    }
}
