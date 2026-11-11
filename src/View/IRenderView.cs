using System;
using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.View;

/// <summary>
/// The render form: a picture that arrives a little at a time and can be looked
/// into, the few dials that decide what it is a picture of, and the things a
/// person does with one.
/// </summary>
/// <remarks>
/// Passive like every other view. It neither traces nor knows what a document is;
/// it says what was asked of it and shows what it is handed.
/// </remarks>
public interface IRenderView
{
    /// <summary>The card was dismissed — by its button, by Escape, or by a click past it.</summary>
    event Action? CloseRequested;

    /// <summary>
    /// A dial moved. Whether that means tracing everything again is not the view's
    /// to decide — two of the dials only change how the result is read.
    /// </summary>
    event Action? DialMoved;

    /// <summary>
    /// The button was pressed: trace it again from nothing, whatever the dials
    /// say. A render is a snapshot of the document as it stood when it started, so
    /// this is the only way to take a second one of a scene that has since moved —
    /// which is what the button is for and what it was silently not doing while it
    /// shared an event with the dials.
    /// </summary>
    event Action? RenderAgainRequested;

    event Action? SaveRequested;

    /// <summary>The card was dragged to a size, and it is worth remembering.</summary>
    event Action<Vector2>? SizeChanged;

    /// <summary>
    /// A point of the picture was pointed at, in its own pixels, to focus the lens
    /// on whatever is there. Raised only while the card is picking.
    /// </summary>
    event Action<int, int>? FocusPicked;

    /// <summary>Whether the card is up. False while it is fading away.</summary>
    bool IsOpen { get; }

    /// <summary>What the fields say right now.</summary>
    RenderDials Dials { get; }

    /// <summary>
    /// Which part of the picture is on screen, in its own pixels, or empty when all
    /// of it is. Not a display question: it is what the renderer is told to trace
    /// first, so a viewer zoomed into a corner converges that corner.
    /// </summary>
    RenderRegion PriorityRegion { get; }

    void Open(RenderDials dials, Vector2 size, bool dialsOpen);

    /// <summary>
    /// Puts values into the fields without raising <see cref="DialMoved"/> — the
    /// same silence <see cref="Open"/> fills them in, because a form being told
    /// what it holds is not somebody turning a dial.
    /// </summary>
    /// <remarks>
    /// What lets an agent set the card's dials over the command surface rather
    /// than by typing into the fields, which is the debug instrument being used as
    /// a way to work. The presenter decides what a change means afterwards, so a
    /// dial set from the wire and one turned by hand take the same path.
    /// </remarks>
    void ShowDials(RenderDials dials);

    void Close();

    /// <summary>Escape and a press past the card, on the settings form's own terms.</summary>
    bool TryDismiss();

    void ShowImage(Texture2D image);

    /// <summary>
    /// How big the picture will be, before there is one — so the viewer fits itself
    /// to it once rather than re-fitting under the eye when the first pass lands.
    /// </summary>
    void ShowSize(int width, int height);

    /// <summary>How far along the render is.</summary>
    void ShowProgress(RenderProgress progress);

    /// <summary>
    /// A line under the picture: what the render could not say about the document,
    /// or why it could not start at all.
    /// </summary>
    void ShowMessage(string message);

    /// <summary>Puts the whole picture back in view — what the Fit button does.</summary>
    void FitImage();

    /// <summary>One screen pixel to one of the render.</summary>
    void ShowActualSize();

    /// <summary>Brings a part of the picture up to fill the panel.</summary>
    void ShowRegion(RenderRegion region);

    /// <summary>Whether the next click on the picture names a point to focus on.</summary>
    bool IsPickingFocus { get; }

    void SetPickingFocus(bool picking);

    /// <summary>
    /// Whether the Focus button may be armed at all. Pushed rather than read off a
    /// dial: what decides it is the blur of the camera the picture is taken
    /// through, and the lens is the camera's rather than this card's.
    /// </summary>
    void SetFocusAvailable(bool available);

    /// <summary>The settings were folded away, or brought back.</summary>
    event Action<bool>? DialsFolded;
}
