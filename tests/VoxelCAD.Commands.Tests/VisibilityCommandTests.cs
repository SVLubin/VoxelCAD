using System.Collections.Generic;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// Hiding as an agent reaches it: 'set-visible' writes the flag, and every element
/// report carries it — reading is half of "reachable", and "why is this not in my
/// export" is the question the report has to be able to answer.
/// </summary>
public sealed class VisibilityCommandTests
{
    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    public VisibilityCommandTests()
    {
        _dispatcher.RunOk("new");
        _dispatcher.RunOk("create-element name=Model kind=VoxelModel");
    }

    [Fact]
    public void AnElementIsReportedVisibleUntilItIsHidden()
    {
        Assert.Equal(true, Visible("Model"));

        _dispatcher.RunOk("set-visible element=Model visible=false");

        Assert.Equal(false, Visible("Model"));
    }

    [Fact]
    public void ShowingItAgainIsTheSameCommand()
    {
        _dispatcher.RunOk("set-visible element=Model visible=false");
        _dispatcher.RunOk("set-visible element=Model visible=true");

        Assert.Equal(true, Visible("Model"));
    }

    /// <summary>The argument is optional, and its absence means "show".</summary>
    [Fact]
    public void WithoutTheArgumentItShows()
    {
        _dispatcher.RunOk("set-visible element=Model visible=false");
        _dispatcher.RunOk("set-visible element=Model");

        Assert.Equal(true, Visible("Model"));
    }

    [Fact]
    public void HidingIsUndoable()
    {
        _dispatcher.RunOk("set-visible element=Model visible=false");
        _dispatcher.RunOk("undo");

        Assert.Equal(true, Visible("Model"));
    }

    /// <summary>A group can be hidden too — it is what a whole assembly hides by.</summary>
    [Fact]
    public void AGroupCanBeHiddenAndTheTreeSaysSo()
    {
        _dispatcher.RunOk("create-element name=Rig");
        _dispatcher.RunOk("set-visible element=Rig visible=false");

        CommandResult tree = _dispatcher.RunOk("tree");
        var elements = (List<object?>)tree.Field("elements")!;

        Assert.Contains(
            elements.Cast<IReadOnlyDictionary<string, object?>>(),
            element => (string)element["name"]! == "Rig" && (bool)element["visible"]! == false);
    }

    [Fact]
    public void ANonsenseValueIsRefusedByName()
    {
        var error = _dispatcher.RunFails("set-visible element=Model visible=maybe");

        Assert.Contains("visible", error);
    }

    private object? Visible(string element) =>
        _dispatcher.RunOk($"element element={element}").Field("visible");
}
