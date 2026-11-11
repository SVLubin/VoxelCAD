using System;
using System.Collections.Generic;
using Godot;

namespace VoxelCAD.Model;

/// <inheritdoc cref="IProjectSnapshot"/>
internal sealed class ProjectSnapshot : IProjectSnapshot
{
    private readonly ProjectDocument _document;
    private readonly List<PendingBlobs> _pendingList;

    private bool _written;

    internal ProjectSnapshot(ProjectDocument document, List<PendingBlobs> pendingList)
    {
        _document = document;
        _pendingList = pendingList;
    }

    public void Write(string path)
    {
        if (_written)
        {
            throw new InvalidOperationException(
                "A snapshot is written once; capture the project again for another file.");
        }

        _written = true;

        ProjectFileStorage.Save(ToDocument(), path);
    }

    /// <summary>
    /// The document with its blobs encoded — the expensive half, done here so it
    /// runs on the writing thread rather than the one that captured.
    /// </summary>
    internal ProjectDocument ToDocument()
    {
        foreach (PendingBlobs pending in _pendingList)
        {
            if (pending.Voxels.Count > 0)
                pending.Element.Voxels = VoxelBlobCodec.Encode(pending.Voxels);
        }

        _pendingList.Clear();

        return _document;
    }

    /// <summary>
    /// A model's cells as copied at the capture, waiting to be encoded into the
    /// element document they belong to.
    /// </summary>
    internal readonly record struct PendingBlobs(
        ElementDocument Element,
        Dictionary<Vector3I, Guid> Voxels);
}
