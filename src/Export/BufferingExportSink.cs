using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelCAD.Export;

/// <summary>
/// A sink that keeps every file in memory until the writer is done, so the whole
/// set can be committed at once — or dropped whole if the writer throws. Keeping
/// the order files are opened in makes the primary come before its companion,
/// which reads well in a result and matters to golden tests.
/// </summary>
internal sealed class BufferingExportSink : IExportSink
{
    private readonly List<(string FileName, MemoryStream Stream)> _filesList = new();

    public BufferingExportSink(string baseName)
    {
        BaseName = baseName;
    }

    public string BaseName { get; }

    public Stream Open(string fileName)
    {
        foreach ((string existing, MemoryStream _) in _filesList)
        {
            if (existing == fileName)
                throw new InvalidOperationException($"The writer opened '{fileName}' twice.");
        }

        var stream = new MemoryStream();
        _filesList.Add((fileName, stream));

        return stream;
    }

    /// <summary>
    /// The finished files, in the order they were opened. <c>ToArray</c> is valid
    /// after the writer has disposed the stream, which is why the buffers can be a
    /// plain <see cref="MemoryStream"/>.
    /// </summary>
    public IReadOnlyList<(string FileName, byte[] Bytes)> Collect()
    {
        var collectedList = new List<(string, byte[])>(_filesList.Count);

        foreach ((string fileName, MemoryStream stream) in _filesList)
            collectedList.Add((fileName, stream.ToArray()));

        return collectedList;
    }
}
