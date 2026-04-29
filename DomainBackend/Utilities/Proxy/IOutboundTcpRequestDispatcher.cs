using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Utilities.Proxy.Models;

namespace Domain.Backend.Utilities.Proxy;

public interface IOutboundTcpRequestDispatcher
{
    Task<OutboundTcpResponse> SendAsync(OutboundTcpRequest request, CancellationToken cancellationToken);
}
