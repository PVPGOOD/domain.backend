namespace Domain.Backend.Utilities.DomainNames;

public interface IDomainNameNormalizer
{
    NormalizedDomain NormalizeExactInput(string input);
    string NormalizeTld(string tld);
}

public sealed record NormalizedDomain(string Domain, string Tld);
