namespace UOAMServer.Domain;

public sealed class Domain
{
    public string Password { get; }
    public Host Host { get; }
    public List<Client> Clients { get; } = new();
    public object Lock { get; } = new();

    public Domain(Host host, string password)
    {
        Host = host;
        Password = password;
    }

    public bool TryAddClient(Client client)
    {
        if (Clients.Count >= Limits.MaxClientsPerDomain)
            return false;

        Clients.Add(client);
        client.Domain = this;
        return true;
    }

    public void RemoveClient(Client client)
    {
        Clients.Remove(client);
        client.Domain = null;
    }

    public bool IsEmpty => Clients.Count == 0;
}
