using System;

namespace StructLayoutEventLibrary
{
    // Built into another assembly: the consumer sees the event, not its hidden field
    public struct LibraryWithEvent { public int A; public event Action Changed; }

    public struct LibraryWithAccessorEvent { public int A; public event Action Changed { add { } remove { } } }
}
