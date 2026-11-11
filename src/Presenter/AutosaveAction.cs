namespace VoxelCAD.Presenter;

/// <summary>What one look at the interval clock decides.</summary>
internal enum AutosaveAction
{
    /// <summary>Not yet, or not while this is going on: the clock stays where it is and asks again next tick.</summary>
    Wait,

    /// <summary>Nothing to write; the clock starts over.</summary>
    Rearm,

    /// <summary>Write a snapshot, then start the clock over.</summary>
    Write,

    /// <summary>The document matches its file again; delete the snapshot and start over.</summary>
    Drop
}
