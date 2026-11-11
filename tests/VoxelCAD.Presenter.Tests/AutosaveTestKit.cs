using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using VoxelCAD.Commands;
using VoxelCAD.CommonTypes;
using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// What every shell-level autosave test needs: preferences with the switch on
/// and a directory of the test's own, a way to wait for a worker write to land,
/// and readers over the directory and the agent's status report.
/// </summary>
internal static class AutosaveTestKit
{
    public static string TempDirectory(string prefix) =>
        Path.Combine(Path.GetTempPath(), $"voxelcad-{prefix}-{Guid.NewGuid():N}");

    public static FakePreferences PreferencesFor(string directory, bool enabled = true, double interval = 120.0)
    {
        var preferences = new FakePreferences { AutosaveDirectory = directory };

        preferences.Apply(preferences.GetValues() with
        {
            AutosaveEnabled = enabled,
            AutosaveIntervalSeconds = interval
        });

        return preferences;
    }

    /// <summary>
    /// A snapshot is written on a worker, so a tick that decided to write has not
    /// written yet. Ticks with no time passing until the write has been booked.
    /// </summary>
    public static void Settle(ShellPresenter shell, ICommandDispatcher dispatcher)
    {
        for (var attempt = 0; attempt < 2000; attempt++)
        {
            shell.Tick(0.0);

            if (!(bool)Document(dispatcher)["writeInFlight"]!)
                return;

            Thread.Sleep(2);
        }

        Assert.Fail("The snapshot write did not land.");
    }

    public static IReadOnlyDictionary<string, object?> Status(ICommandDispatcher dispatcher)
    {
        CommandResult result = dispatcher.Execute("autosave-status", new Dictionary<string, string>());

        Assert.True(result.Ok, result.Error);

        return (IReadOnlyDictionary<string, object?>)result.Value!;
    }

    public static IReadOnlyDictionary<string, object?> Document(ICommandDispatcher dispatcher) =>
        (IReadOnlyDictionary<string, object?>)Status(dispatcher)["document"]!;

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Snapshots(ICommandDispatcher dispatcher)
    {
        var rowsList = new List<IReadOnlyDictionary<string, object?>>();

        foreach (object? row in (System.Collections.IEnumerable)Status(dispatcher)["snapshots"]!)
            rowsList.Add((IReadOnlyDictionary<string, object?>)row!);

        return rowsList;
    }

    public static string[] Documents(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*" + AutosaveStore.DocumentExtension)
            : Array.Empty<string>();

    public static string[] Sidecars(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*" + AutosaveStore.SidecarExtension)
            : Array.Empty<string>();

    public static string[] Locks(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*" + AutosaveStore.LockExtension)
            : Array.Empty<string>();

    /// <summary>Removes the test's directory — or the file a failure test planted where it should be.</summary>
    public static void Delete(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        else if (File.Exists(directory))
            File.Delete(directory);
    }

    public static string? SidecarOriginalPath(string directory)
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Sidecars(directory)[0]));
        System.Text.Json.JsonElement path = document.RootElement.GetProperty("originalPath");

        return path.ValueKind == System.Text.Json.JsonValueKind.Null ? null : path.GetString();
    }

    /// <summary>The material row of a fresh document, which is where a colour gesture is opened.</summary>
    public static SelectionTarget Material(FakeShellView view) =>
        new(SelectionKind.Material, view.SceneView.BrushMaterialId);
}
