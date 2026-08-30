namespace Kassyi.GSharp.Core;

/// <summary>
/// Indicates that a parameter takes ownership of a disposable resource, requiring the callee/container to manage its lifecycle.
/// </summary>
/// <remarks><para>Implements: <b>MEM-02</b></para></remarks>
#pragma warning disable IDE0051 // Unused member
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class TakesOwnershipAttribute : Attribute;
