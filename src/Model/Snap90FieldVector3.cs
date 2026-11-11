using Godot;
using VoxelCAD.CommonTypes;

namespace VoxelCAD.Model;

/// <summary>
/// Rotation constrained to right angles. Stores quarter turns rather than
/// degrees, so 90°-alignment is structural and full turns wrap without drift.
/// </summary>
internal sealed class Snap90FieldVector3 : IFieldVector3
{
    private const float DegreesPerQuarter = 90f;
    private const int QuartersPerTurn = 4;

    private Vector3I _quarters;

    public Snap90FieldVector3(Vector3 degrees)
    {
        Set(degrees);
    }

    public FieldMode Mode => FieldMode.Snap90;

    public Vector3 AsFloat => (Vector3)_quarters * DegreesPerQuarter;

    public Vector3I AsInt => _quarters * (int)DegreesPerQuarter;

    public void Set(Vector3 degrees) =>
        _quarters = Wrap(RoundingHelper.ToNearest(degrees / DegreesPerQuarter));

    public void Set(Vector3I degrees) => Set((Vector3)degrees);

    /// <summary>
    /// Full turns wrapped away, into (-180°, 180°] — quarter turns -1, 0, 1, 2.
    /// </summary>
    /// <remarks>
    /// Without this the same orientation had several spellings: a matrix round
    /// trip through a reparent hands back -180° where 180° went in, and negative
    /// zero where zero did, so the inspector's numbers changed while nothing
    /// moved. Storing quarter turns already kept the value from drifting; this is
    /// the other half of the promise, that it reads the same way every time.
    /// </remarks>
    private static Vector3I Wrap(Vector3I quarters) =>
        new(Wrap(quarters.X), Wrap(quarters.Y), Wrap(quarters.Z));

    private static int Wrap(int quarters)
    {
        int wrapped = quarters % QuartersPerTurn;

        if (wrapped > 2)
            return wrapped - QuartersPerTurn;

        if (wrapped < -1)
            return wrapped + QuartersPerTurn;

        return wrapped;
    }
}
