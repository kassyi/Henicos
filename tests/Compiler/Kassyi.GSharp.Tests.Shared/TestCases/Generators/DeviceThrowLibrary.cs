using System;
using Kassyi.GSharp.Core;

// A library whose device function throws an exception type of its own: the throw's site travels in the record
namespace DeviceThrowCase
{
    public sealed class LibraryThrowException : Exception
    {
        public LibraryThrowException(string message)
            : base(message)
        {
        }
    }

    public static partial class LibThrow
    {
        [GpuDeviceFunction]
        public static int Check(int v)
        {
            if (v < 0)
            {
                throw new LibraryThrowException("from the library");
            }

            return v;
        }
    }
}
