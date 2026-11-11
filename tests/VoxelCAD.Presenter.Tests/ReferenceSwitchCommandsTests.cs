using System.Collections.Generic;
using VoxelCAD.Commands;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// The one switch that says whether references are drawn. Reading is free, on a
/// slice's own terms; turning it changes what the user is looking at, so it is
/// gated like an aim.
/// </summary>
public sealed class ReferenceSwitchCommandsTests
{
    private const string Client = "reference-client";

    private readonly ControlArbiter _arbiter = new();
    private readonly EditorSession _session = new();
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly ICommandDispatcher _dispatcher;

    public ReferenceSwitchCommandsTests()
    {
        var hostCommands = new List<CommandDefinition>(ReferenceSwitchCommands.Create(_session));

        ICommandDispatcher inner = CommandFactory.CreateDispatcher(
            new CommandContext(_project),
            hostCommands);

        _dispatcher = new ControlGatedDispatcher(inner, _arbiter, _session, Client);
    }

    /// <summary>
    /// An agent judging a capture has to be able to find out why the drawing it
    /// placed is not in the picture, and it must not have to hold the editor to ask.
    /// </summary>
    [Fact]
    public void ReadingIsFreeAndStartsAtDrawn()
    {
        CommandResult result = Run("references-visible");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(true, Field(result, "visible"));
    }

    [Fact]
    public void TurningItIsRefusedWithoutControl()
    {
        CommandResult result = Run("set-references-visible", ("visible", "false"));

        Assert.False(result.Ok);
        Assert.Contains("take-control", result.Error);
        Assert.True(_session.ReferencesVisible);
    }

    [Fact]
    public void WithControlItTurnsAndReportsWhatItBecame()
    {
        _arbiter.Request(Client, "Claude", string.Empty);
        _arbiter.Grant();

        CommandResult result = Run("set-references-visible", ("visible", "false"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(false, Field(result, "visible"));
        Assert.False(_session.ReferencesVisible);

        Assert.Equal(false, Field(Run("references-visible"), "visible"));
    }

    private static object? Field(CommandResult result, string name) =>
        ((IReadOnlyDictionary<string, object?>)result.Value!)[name];

    private CommandResult Run(string name, params (string Name, string Value)[] arguments)
    {
        var byName = new Dictionary<string, string>();

        foreach ((string argument, string value) in arguments)
            byName[argument] = value;

        return _dispatcher.Execute(name, byName);
    }
}
