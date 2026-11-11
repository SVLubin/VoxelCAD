using System;
using System.Collections.Generic;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Undo history of a scene. Commands themselves are internal — callers change the
/// model through ordinary <see cref="IScene"/> methods and the model records the
/// action itself, so no caller can forget to make something undoable.
/// </summary>
public interface IHistory
{
    /// <summary>
    /// Carries which record moved and which scene it belongs to, so that undoing
    /// an action in a scene the user is not looking at can bring that scene up
    /// rather than appearing to do nothing.
    /// </summary>
    event Action<HistoryChange> Changed;

    /// <summary>
    /// Who new records are attributed to. Set it when control passes to an agent
    /// and back when it returns, so the panel can show who did what. Not undoable
    /// and not part of the document — it is stamped onto records as they are filed.
    /// </summary>
    HistoryActor CurrentActor { get; set; }

    /// <summary>Oldest first. Everything after <see cref="CurrentIndex"/> is undone.</summary>
    IReadOnlyList<HistoryRecord> Records { get; }

    /// <summary>Index of the last applied record, or -1 when nothing is applied.</summary>
    int CurrentIndex { get; }

    bool CanUndo { get; }

    bool CanRedo { get; }

    void Undo();

    void Redo();

    /// <summary>
    /// Moves to the state just after record <paramref name="index"/>, undoing or
    /// redoing as many records as needed. Pass -1 to undo everything.
    /// </summary>
    void UndoTo(int index);

    /// <summary>
    /// Collapses everything done until the returned scope is disposed into one
    /// record — a mouse drag is one undo step, not two hundred.
    /// </summary>
    /// <param name="sceneId">
    /// The scene the gesture happens in, or <see cref="Guid.Empty"/> for a
    /// project-level one. A batch is filed as a single record, so it has to be
    /// told what the individual commands would otherwise have said.
    /// </param>
    IDisposable CreateBatch(ActionKind kind, Guid sceneId, Guid targetId, string targetName);
}
