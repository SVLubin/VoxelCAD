using VoxelCAD.Presenter;
using Xunit;

namespace VoxelCAD.Presenter.Tests;

/// <summary>
/// How the banner's running clock reads: bare seconds while that is easy to grasp,
/// then minutes and seconds. It counts time completed, so it floors.
/// </summary>
public sealed class ElapsedTimeHelperTests
{
    [Theory]
    [InlineData(0.0, "0s")]
    [InlineData(5.0, "5s")]
    [InlineData(5.9, "5s")]
    [InlineData(59.0, "59s")]
    [InlineData(60.0, "1:00")]
    [InlineData(65.0, "1:05")]
    [InlineData(125.0, "2:05")]
    [InlineData(600.0, "10:00")]
    public void ItReadsAtAGlance(double seconds, string expected)
    {
        Assert.Equal(expected, ElapsedTimeHelper.Format(seconds));
    }

    [Fact]
    public void ANegativeIsNeverShown()
    {
        Assert.Equal("0s", ElapsedTimeHelper.Format(-3.0));
    }
}
