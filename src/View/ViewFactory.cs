using Godot;

namespace VoxelCAD.View;

/// <summary>
/// The view assembly's only public entry point. It builds the shell and attaches
/// it; every panel below is reached through the interfaces the shell hands out.
/// </summary>
public static class ViewFactory
{
    public static IShellView CreateShell(Node host)
    {
        // One theme, repainted in place, is the whole app's appearance — nothing
        // below carries its own colours, and the 3D viewport (a Node3D) is
        // untouched. It goes on the root window so dialog subwindows inherit it,
        // and the shell applies the same instance to the Control that roots its
        // panels, because theme inheritance does not cross the shell's
        // CanvasLayer. The skin owns it so the settings form can change the look
        // without any of that wiring being redone.
        var skin = new EditorSkin();
        host.GetTree().Root.Theme = skin.Theme;

        // The viewport surface belongs to both and to neither: the scene view
        // reads its input, the shell decides where on screen it sits. Building it
        // here is what lets each take the half it owns without either reaching
        // into the other.
        // Who is driving decides whose pointer and keys the tools read. Built
        // here for the same reason the surface is: it belongs to the viewport and
        // to the shell equally, and to neither of them alone.
        var injected = new InjectedInputState();

        // The pace, owned by both: the shell is told what the user picked, and
        // the scene's camera turn runs at the same speed.
        var timing = new MotionTiming();
        var input = new EditorInputState(new PhysicalInputState(host), injected);

        var surface = new ViewportSurface(input);

        // Built here for exactly the reason the surface is, and belonging to the
        // same two owners: the scene view turns it and answers its presses, the
        // shell decides which corner of the window it sits in. It reads the driving
        // party's pointer like every other tool, and that is this factory's to hand
        // out.
        var viewCube = new ViewCube(input);

        // And again for the drag readout, the third thing owned by both: the scene
        // view writes its line, the shell decides which corner of the viewport it
        // occupies.
        var dragReadout = new DragReadout();

        // The 3D layer goes in first so the docks, which live on a CanvasLayer,
        // draw over it.
        var scene = new SceneView(surface, input, viewCube, dragReadout, timing, skin)
        {
            Name = "SceneView"
        };
        host.AddChild(scene);

        var shell = new ShellView(scene, surface, viewCube, dragReadout, skin, injected, input, timing)
        {
            Name = "EditorShell"
        };
        host.AddChild(shell);

        // The shell is the chrome layer the scene view hides for a clean capture.
        // It is built second — around the scene — so the wiring is completed here
        // rather than passed through the constructor.
        scene.SetChrome(shell);

        // And the shell is what knows whether chrome stands over the viewport —
        // a rail's temporary panel — so the scene's polled affordances ask it.
        scene.SetChromeClaims(shell.IsPointOverChrome);

        return shell;
    }
}
