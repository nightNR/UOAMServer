namespace UOAMServer;

public sealed class CliArgs
{
    public int Port { get; private set; } = 2000;
    public string? PasswordFile { get; private set; }
    public int Verbosity { get; private set; } = 1;

    public static CliArgs Parse(string[] args)
    {
        var result = new CliArgs();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port":
                    result.Port = int.Parse(args[++i]);
                    break;
                case "--password":
                    result.PasswordFile = args[++i];
                    break;
                case "--verbose":
                    result.Verbosity++;
                    break;
                case "--quiet":
                    result.Verbosity = 0;
                    break;
            }
        }

        return result;
    }
}
