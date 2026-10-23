// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.LocalOwnership;

public class OwnedResource : IDisposable
{
    public void Compute() { }
    public void Dispose() { }
}

public class NotDisposable
{
}

public class Wrapper(OwnedResource resource)
{
    public OwnedResource Resource => resource;
}

public readonly struct WrapperStruct(OwnedResource resource)
{
    public OwnedResource Resource => resource;
}

public class LocalOwnershipValid
{
    private OwnedResource? _field;

    public void UsingDeclaration()
    {
        using var ctx = new OwnedResource();
        ctx.Compute();
    }

    public void UsingStatement()
    {
        using (var ctx = new OwnedResource())
        {
            ctx.Compute();
        }
    }

    public void ExplicitDispose()
    {
        var ctx = new OwnedResource();
        ctx.Dispose();
    }

    public void NullConditionalDispose()
    {
        var ctx = new OwnedResource();
        ctx?.Dispose();
    }

    public void TryFinally()
    {
        var ctx = new OwnedResource();
        try
        {
            ctx.Compute();
        }
        finally
        {
            ctx.Dispose();
        }
    }

    public OwnedResource ReturnTransfer()
    {
        var ctx = new OwnedResource();
        return ctx;
    }

    public OwnedResource ReturnNewTransfer()
    {
        return new OwnedResource();
    }

    public void OutParameter(out OwnedResource ctx)
    {
        ctx = new OwnedResource();
    }

    public void FieldAssignment()
    {
        var ctx = new OwnedResource();
        _field = ctx;
    }

    public void TakesOwnershipArgument()
    {
        var ctx = new OwnedResource();
        Consume(ctx);
    }

    public Wrapper ConstructorArgument()
    {
        var ctx = new OwnedResource();
        return new Wrapper(ctx);
    }

    public WrapperStruct StructConstructorArgument()
    {
        var ctx = new OwnedResource();
        return new WrapperStruct(ctx);
    }

    public void ExistingUsingOnExpression()
    {
        var ctx = new OwnedResource();
        using (ctx)
        {
            ctx.Compute();
        }
    }

    public void CapturedByLambda()
    {
        var ctx = new OwnedResource();
        Action dispose = () => ctx.Dispose();
        dispose();
    }

    public void NotDisposableLocal()
    {
        var plain = new NotDisposable();
    }

    public void NotCreatedHere(OwnedResource borrowed)
    {
        var alias = borrowed;
        alias.Compute();
    }

    private static void Consume([TakesOwnership] OwnedResource resource) => resource.Dispose();
}
