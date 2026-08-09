namespace UOAMServer.Domain;

public sealed class Host
{
    public string? LockedPassword { get; }
    public List<Domain> Domains { get; } = new();
    public object Lock { get; } = new();

    public Host(string? lockedPassword)
    {
        LockedPassword = lockedPassword;
    }

    public Domain? DomainZero { get; private set; }

    public Domain CreateDomainZero()
    {
        var domain = new Domain(this, string.Empty);
        Domains.Add(domain);
        DomainZero = domain;
        return domain;
    }

    public Domain? GetDomainByPassword(string password) =>
        Domains.FirstOrDefault(d => d.Password == password);

    public Domain? CreateDomain(string password)
    {
        if (Domains.Count >= Limits.MaxDomains)
            return null;

        var domain = new Domain(this, password);
        Domains.Add(domain);
        return domain;
    }

    public void RemoveDomain(Domain domain) => Domains.Remove(domain);

    public Client? FindClientById(uint id)
    {
        foreach (var domain in Domains)
        {
            lock (domain.Lock)
            {
                var found = domain.Clients.FirstOrDefault(c => c.Id == id);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }
}
