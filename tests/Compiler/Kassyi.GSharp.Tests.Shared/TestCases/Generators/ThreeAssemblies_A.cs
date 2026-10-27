using System;
using Kassyi.GSharp.Core;

// The first library: structs that its records hold, a computed property and a Dispose
namespace XasmA
{
    public struct Vec2
    {
        public float X;
        public float Y;

        public float Sum => X + Y;
    }

    public struct Lease : IDisposable
    {
        public int V;

        public void Dispose()
        {
            V = 0;
        }
    }
}
