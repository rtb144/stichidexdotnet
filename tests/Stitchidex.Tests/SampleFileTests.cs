using Stitchidex.Core.Parsers;
using Xunit;

namespace Stitchidex.Tests;

public class SampleFileTests
{
    static string SamplesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "samples"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "samples");
    }

    [Theory]
    [InlineData("scene-wilcom-v4.pes")]
    [InlineData("random2-ew.dst")]
    public void RealSampleFilesParse(string name)
    {
        var path = Path.Combine(SamplesDir(), name);
        Assert.True(File.Exists(path));
        var p = EmbroideryParserRegistry.ParseFile(path);
        Assert.NotNull(p);
    }
}
