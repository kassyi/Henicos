namespace Kassyi.GSharp.Cryptography;

/// <summary>
/// Result of <see cref="Ed25519PublicKeyValidator.Validate"/>.
/// </summary>
internal enum Ed25519PublicKeyStatus
{
    Valid,
    InvalidLength,
    NotOnCurve,
    SmallOrder,
}
