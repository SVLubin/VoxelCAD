namespace VoxelCAD.CommonTypes;

/// <summary>
/// The chrome panels the shell lays out. The tool strip is deliberately not
/// here — tools are buttons of the current element's panel, inside the
/// inspector.
/// </summary>
public enum PanelKind
{
    Toolbar,
    Project,
    History,
    Inspector
}
