using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Model;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// What the render commands promise about themselves. Not what they render —
/// that needs a device — but the half a reader has to be able to trust before it
/// asks for one: the numbers in the descriptions, and the report that exists so
/// nobody has to find the bounds by being refused.
/// </summary>
public sealed class RenderCommandsTests : IDisposable
{
    private readonly IProject _project = ModelFactory.CreateProject();
    private readonly FakeSceneView _view = new();
    private readonly FakeRenderView _card = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakePreferences _preferences = new();
    private readonly FakeRenderJobs _jobs = new();
    private readonly RenderPresenter _presenter;
    private readonly ICommandDispatcher _dispatcher;

    public RenderCommandsTests()
    {
        _presenter = new RenderPresenter(
            _card,
            _view,
            new CommandContext(_project),
            _dialogs,
            _preferences,
            _jobs);

        var hostCommands = new List<CommandDefinition>();
        hostCommands.AddRange(RenderCommands.Create(_presenter));

        _dispatcher = CommandFactory.CreateDispatcher(
            new CommandContext(_project),
            hostCommands);
    }

    public void Dispose() => _presenter.Dispose();

    /// <summary>
    /// The descriptions carry numbers now — the defaults and every bound — read
    /// off <c>RenderLimits</c> rather than written down beside it, because
    /// restated they agreed until somebody changed one. What that buys has to be
    /// paid for here: an interpolated number is formatted in the machine's own
    /// locale, and this project has already lost a shader to a decimal comma.
    /// </summary>
    [Fact]
    public void NoDescriptionCarriesADecimalCommaWhateverTheLocaleIs()
    {
        // On a thread of its own rather than by setting this one's culture: xunit
        // runs classes in parallel, so a culture left on a pool thread — even for
        // the length of a try block — is another test formatting its failure
        // message in Russian, which is exactly what happened.
        var summaries = new List<string>();

        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");

            foreach (CommandDescriptor descriptor in Describe())
            {
                foreach (CommandParameter parameter in descriptor.Parameters)
                    summaries.Add(parameter.Summary);
            }
        });

        thread.Start();
        thread.Join();

        Assert.NotEmpty(summaries);

        foreach (string summary in summaries)
            Assert.DoesNotMatch(@"\d,\d", summary);
    }

    /// <summary>
    /// A default outside its own bound is a command that refuses what it says it
    /// will do when asked for nothing. It could happen: the defaults and the
    /// limits are separate constants, and only this compares them.
    /// </summary>
    [Fact]
    public void EveryReportedDefaultIsInsideItsReportedLimit()
    {
        CommandResult result = _dispatcher.Execute("render-settings", new Dictionary<string, string>());

        Assert.True(result.Ok, result.Error);

        var defaults = (Dictionary<string, object?>)Answer(result)["defaults"]!;
        var limits = (Dictionary<string, object?>)Answer(result)["limits"]!;

        foreach ((string name, object? bound) in limits)
        {
            if (!defaults.TryGetValue(name, out object? value) || value is null)
                continue;

            double[] pair = ((Array)bound!).Cast<object>()
                .Select(x => Convert.ToDouble(x, CultureInfo.InvariantCulture))
                .ToArray();

            double actual = Convert.ToDouble(value, CultureInfo.InvariantCulture);

            Assert.True(
                actual >= pair[0] && actual <= pair[1],
                $"The reported default for '{name}' is {actual}, outside its own "
                    + $"reported limit of {pair[0]} to {pair[1]}.");
        }
    }

    /// <summary>
    /// The report is what a caller reads instead of finding a bound by being
    /// refused, so a dial the command takes and the report does not mention is a
    /// bound nobody can discover.
    /// </summary>
    [Fact]
    public void TheReportNamesTheSizeAndSampleBoundsTheCommandIsHeldTo()
    {
        CommandResult result = _dispatcher.Execute("render-settings", new Dictionary<string, string>());
        var limits = (Dictionary<string, object?>)Answer(result)["limits"]!;

        foreach (string dial in new[] { "size", "samples", "bounces", "exposure", "glare" })
            Assert.True(limits.ContainsKey(dial), $"'{dial}' has a bound and the report omits it.");
    }

    /// <summary>
    /// The lens's bounds are reported here too, though no argument of this command
    /// takes one: a reader asking what a render may be is asking about the whole
    /// picture, and being sent to a second report for the blur would be exactly the
    /// bound-by-refusal this command exists to avoid.
    /// </summary>
    [Fact]
    public void TheReportAlsoNamesTheLensBoundsEvenThoughTheLensIsTheCameras()
    {
        CommandResult result = _dispatcher.Execute("render-settings", new Dictionary<string, string>());
        var limits = (Dictionary<string, object?>)Answer(result)["cameraLimits"]!;

        foreach (string dial in new[] { "fov", "blur", "focus", "orthoHeight" })
            Assert.True(limits.ContainsKey(dial), $"'{dial}' has a bound and the report omits it.");
    }

    /// <summary>
    /// Every render command is registered even where it cannot run, on the
    /// injection commands' own precedent: a client caches the tool list it was
    /// given at connection, so a command that appears only sometimes is one an
    /// agent cannot discover.
    /// </summary>
    [Fact]
    public void TheWholeCardIsOnTheSurface()
    {
        string[] named = Describe().Select(d => d.Name).ToArray();

        foreach (string command in new[]
                 {
                     "render", "render-settings", "render-open", "render-close",
                     "render-view", "render-focus", "render-image", "render-status",
                     "render-set", "render-again"
                 })
        {
            Assert.Contains(command, named);
        }
    }

    /// <summary>
    /// Looking is not editing: a render reads the document and touches nothing, so
    /// none of the reads may demand control of the editor.
    /// </summary>
    [Fact]
    public void LookingAtARenderDoesNotMutate()
    {
        IReadOnlyList<CommandDescriptor> described = Describe();

        foreach (string command in new[] { "render", "render-settings", "render-image", "render-status" })
            Assert.False(described.Single(d => d.Name == command).Mutates, command);
    }

    /// <summary>
    /// The card's dials had no way in from the surface at all, so an agent that
    /// wanted a different picture had to type into the fields — the debug
    /// instrument being used as a way to work, which the canon forbids in so many
    /// words.
    /// </summary>
    [Fact]
    public void SettingADialFromTheSurfaceChangesWhatIsBeingTraced()
    {
        _presenter.Open();

        CommandResult result = Run("render-set", ("width", "640"), ("samples", "2000"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(640, _card.Dials.Width);
        Assert.Equal(2000, _card.Dials.Samples);

        // A width is what is traced, so it starts again; the samples ride along.
        Assert.Equal(2, _jobs.Starts);
        Assert.Equal(640, _jobs.Started[1].Width);
    }

    /// <summary>
    /// Named rather than whole, so one dial can be turned without restating the
    /// twelve beside it — and so a call does not silently undo what a hand set a
    /// moment ago.
    /// </summary>
    [Fact]
    public void WhatIsNotNamedIsLeftWhereTheCardHadIt()
    {
        _presenter.Open();
        _presenter.ApplyDials(_card.Dials with { Glare = 0.2f, SunAzimuthDegrees = 33f });

        Run("render-set", ("width", "512"));

        Assert.Equal(0.2f, _card.Dials.Glare, 3);
        Assert.Equal(33f, _card.Dials.SunAzimuthDegrees, 3);
    }

    /// <summary>
    /// Exposure is applied when the picture is read, so it re-grades what has been
    /// traced — the same bargain a hand gets, because it goes down the same path.
    /// </summary>
    [Fact]
    public void SettingExposureFromTheSurfaceDoesNotThrowTheRenderAway()
    {
        _presenter.Open();

        FakeRenderJob job = _jobs.Last!;

        Run("render-set", ("exposure", "2"));

        Assert.Equal(1, _jobs.Starts);
        Assert.False(job.Disposed);
        Assert.Equal(2f, job.Exposure);
    }

    /// <summary>
    /// A render is a snapshot, so a reader that sees 'stale' needs something to do
    /// about it — and closing and reopening is not it: that refits the viewer,
    /// where this keeps it.
    /// </summary>
    [Fact]
    public void RenderingAgainIsReachableAndClearsTheStaleNotice()
    {
        _presenter.Open();

        _project.Scenes[0].CreateElement(
            _project.Scenes[0].Id, ComponentKind.VoxelModel, "Block");

        Assert.True(_presenter.IsStale);

        CommandResult result = Run("render-again");

        Assert.True(result.Ok, result.Error);
        Assert.False(_presenter.IsStale);
        Assert.Equal(2, _jobs.Starts);
    }

    /// <summary>
    /// Both refuse plainly with no card up, on 'render-view' and 'render-focus''s
    /// own terms — and name the command that would open one.
    /// </summary>
    [Theory]
    [InlineData("render-set")]
    [InlineData("render-again")]
    public void BothRefuseWithNoCardOpenAndSayHowToOpenOne(string command)
    {
        CommandResult result = Run(command);

        Assert.False(result.Ok);
        Assert.Contains("render-open", result.Error);
    }

    /// <summary>
    /// The answer is read back off the card rather than echoed from the request,
    /// which is what lets it differ: a field has a step, and a value off it is
    /// snapped to the nearest one on it. (That snapping is the real card's, so what
    /// is held here is only that the report follows the card at all.)
    /// </summary>
    [Fact]
    public void TheAnswerIsReadBackOffTheCard()
    {
        _presenter.Open();

        CommandResult result = Run("render-set", ("width", "512"), ("samples", "64"));

        Assert.Equal(512, Convert.ToInt32(Answer(result)["width"]));
        Assert.Equal(64, Convert.ToInt32(Answer(result)["samplesWanted"]));
    }

    private CommandResult Run(string name, params (string Key, string Value)[] arguments)
    {
        var map = new Dictionary<string, string>();

        foreach ((string key, string value) in arguments)
            map[key] = value;

        return _dispatcher.Execute(name, map);
    }

    private IReadOnlyList<CommandDescriptor> Describe() =>
        RenderCommands.Create(_presenter).Select(d => d.Descriptor).ToList();

    private static Dictionary<string, object?> Answer(CommandResult result) =>
        (Dictionary<string, object?>)result.Value!;
}
