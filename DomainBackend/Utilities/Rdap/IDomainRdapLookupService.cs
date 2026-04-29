using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Tasks.Search.Models;
using Domain.Backend.Tasks.Whois.Models;

namespace Domain.Backend.Utilities.Rdap;

public interface IDomainRdapLookupService
{
    Task<DomainAvailabilityResult?> TryLookupAsync(DomainCandidate candidate, CancellationToken cancellationToken);
}
