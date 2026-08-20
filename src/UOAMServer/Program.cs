using UOAMServer;
using UOAMServer.Domain;
using UOAMServer.Server;

var cliArgs = CliArgs.Parse(args);
Log.DebugEnabled = cliArgs.Verbosity >= 2;

string? lockedPassword = null;
if (cliArgs.PasswordFile is not null)
{
    lockedPassword = (await File.ReadAllLinesAsync(cliArgs.PasswordFile)).FirstOrDefault()?.Trim();
    if (string.IsNullOrEmpty(lockedPassword))
    {
        Console.Error.WriteLine($"password file '{cliArgs.PasswordFile}' is empty");
        return 1;
    }
}

var host = new Host(lockedPassword);
host.CreateDomainZero();

Console.WriteLine($"starting UOAMServer on port {cliArgs.Port}");

var server = new UoamServer(host, cliArgs.Port, responseDelayMs: cliArgs.ResponseDelayMs);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await server.RunAsync(cts.Token);
return 0;
