using System;
using System.Security.Cryptography;

namespace VoxelCAD.Model;

/// <summary>
/// The id an element of a prefab instance's contents wears: a hash of the instance
/// it belongs to and the source element it stands for.
/// </summary>
/// <remarks>
/// Derived rather than minted, because the contents are rebuilt from scratch every
/// time the source changes. A fresh <see cref="Guid"/> per rebuild would drop the
/// selection, the expansion state of the tree and every id an agent had just read
/// back, on every keystroke inside the source. Nothing persists these — instance
/// contents are not written to a file — so the only promise this has to keep is
/// that the same pair answers the same way inside one document.
/// </remarks>
internal static class PrefabIdentity
{
    public static Guid Derive(Guid instanceId, Guid sourceElementId)
    {
        Span<byte> pair = stackalloc byte[32];

        instanceId.TryWriteBytes(pair[..16]);
        sourceElementId.TryWriteBytes(pair[16..]);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(pair, hash);

        Span<byte> id = hash[..16];

        // RFC 4122's byte layout: version in the high nibble of byte six, variant
        // in the top bits of byte eight. Nothing here reads either, and only the
        // variant survives — Guid(ReadOnlySpan<byte>) reads the first three fields
        // little-endian, so the version nibble the printed form shows comes from
        // byte seven and is whatever the hash put there. Left as it is: these ids
        // are stable identity across rebuilds and reloads, so changing how they
        // are computed would re-key every instance in every saved document, and
        // the bits themselves do no work — what keeps a derived id clear of a
        // minted one is 128 bits of SHA-256, not four of them.
        id[6] = (byte)((id[6] & 0x0F) | 0x50);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);

        return new Guid(id);
    }
}
