using VoxelCAD.Commands;
using VoxelCAD.Model;
using Xunit;

namespace VoxelCAD.Commands.Tests;

/// <summary>
/// <c>new sample=</c>: the agent's route to what a first launch opens. The sample
/// arrives untitled, so a save has to be told where and the shipped copy is never
/// written over.
/// </summary>
public sealed class SampleCommandTests
{
    private readonly CommandContext _context = new();
    private readonly ICommandDispatcher _dispatcher;

    public SampleCommandTests()
    {
        StandardMaterials.Install();
        _dispatcher = CommandFactory.CreateDispatcher(_context);
    }

    [Fact]
    public void ASampleOpensUntitled()
    {
        _dispatcher.RunOk("new sample=castle");

        Assert.Null(_context.Path);
        Assert.Equal("Castle", _context.Project.Scenes[0].Name);
        Assert.Empty(_context.Project.History.Records);
    }

    [Fact]
    public void ASaveOfASampleHasToBeToldWhere()
    {
        _dispatcher.RunOk("new sample=castle");

        Assert.Contains("pass 'path'", _dispatcher.RunFails("save"));
    }

    [Fact]
    public void AnUnknownSampleIsRefusedNamingTheOnesThereAre()
    {
        Assert.Contains("castle", _dispatcher.RunFails("new sample=fortress"));
    }

    [Fact]
    public void ASampleBringsItsOwnRoom()
    {
        Assert.Contains("own room", _dispatcher.RunFails("new sample=castle size=8,8,8"));
    }
}
