namespace VoxelCAD.CommonTypes;

/// <summary>How the user answered a <see cref="ConfirmRequest"/>.</summary>
/// <remarks>
/// <see cref="Cancel"/> is first so that the default — a dialog dismissed with the
/// window button, or a request that never reached a user — is the answer that
/// loses nothing.
/// </remarks>
public enum ConfirmResult
{
    /// <summary>Do not go through with it. Also what dismissing the dialog means.</summary>
    Cancel,

    /// <summary>Go through with it, doing the thing the question offered first.</summary>
    Confirm,

    /// <summary>Go through with it, throwing the work away.</summary>
    Deny
}
