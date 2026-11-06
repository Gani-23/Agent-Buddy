namespace AgentBuddy.Models;

public sealed record NetworkEndpointOption(
    string Name,
    string Host,
    string Url,
    bool IsDynamicIp,
    string Description)
{
    public override string ToString() => Name;
}
