using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Tasks.Search.Models;
using Domain.Backend.Tasks.Whois.Models;

namespace Domain.Backend.Tasks.Whois;

public interface IDomainAvailabilityProvider
{
    Task<DomainAvailabilityResult> CheckAsync(DomainCandidate candidate, CancellationToken cancellationToken);
}
