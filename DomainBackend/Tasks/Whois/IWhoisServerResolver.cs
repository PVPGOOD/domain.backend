using System.Collections.Generic;

namespace Domain.Backend.Tasks.Whois;

public interface IWhoisServerResolver
{
    string? ResolveServer(string tld);
    IReadOnlyList<string> GetSupportedTlds();
    IReadOnlyList<SupportedTldInfo> GetSupportedTldInfos();
}

public sealed record SupportedTldInfo(string Tld, string Category);
