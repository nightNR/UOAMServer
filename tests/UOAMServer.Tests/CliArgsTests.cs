using UOAMServer;
using Xunit;

namespace UOAMServer.Tests;

public class CliArgsTests
{
    [Fact]
    public void Parse_NoArgs_UsesDefaults()
    {
        var args = CliArgs.Parse(Array.Empty<string>());

        Assert.Equal(2000, args.Port);
        Assert.Null(args.PasswordFile);
        Assert.Equal(1, args.Verbosity);
    }

    [Fact]
    public void Parse_PortFlag_OverridesDefault()
    {
        var args = CliArgs.Parse(new[] { "--port", "2222" });

        Assert.Equal(2222, args.Port);
    }

    [Fact]
    public void Parse_PasswordFlag_SetsPasswordFile()
    {
        var args = CliArgs.Parse(new[] { "--password", "/etc/uoam/pw" });

        Assert.Equal("/etc/uoam/pw", args.PasswordFile);
    }

    [Fact]
    public void Parse_VerboseFlag_IncrementsVerbosity()
    {
        var args = CliArgs.Parse(new[] { "--verbose", "--verbose" });

        Assert.Equal(3, args.Verbosity);
    }
}
