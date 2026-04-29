using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Utilities.Proxy.Models;

namespace Domain.Backend.Utilities.Proxy;

public sealed class HttpProxyManagerOutboundTcpRequestDispatcher(HttpClient httpClient) : IOutboundTcpRequestDispatcher
{
    public async Task<OutboundTcpResponse> SendAsync(OutboundTcpRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/v1/proxy/tcp/send", new ProxyTcpRequestDto
        {
            Host = request.Host,
            Port = request.Port,
            Payload = request.Payload,
            Timeout = request.Timeout,
            Purpose = request.Purpose
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new OutboundTcpResponse
            {
                Success = false,
                ErrorMessage = $"ProxyManager API returned HTTP {(int)response.StatusCode} {response.StatusCode}.",
                ProxyId = "proxy-manager-api"
            };
        }

        var proxyResponse = await response.Content.ReadFromJsonAsync<ProxyTcpResponseDto>(cancellationToken);
        if (proxyResponse is null)
        {
            return new OutboundTcpResponse
            {
                Success = false,
                ErrorMessage = "ProxyManager API returned an empty response.",
                ProxyId = "proxy-manager-api"
            };
        }

        return new OutboundTcpResponse
        {
            Success = proxyResponse.Success,
            ResponseText = proxyResponse.ResponseText,
            ErrorMessage = proxyResponse.ErrorMessage,
            ElapsedMs = proxyResponse.ElapsedMs,
            ProxyId = proxyResponse.ProxyId,
            WorkerId = proxyResponse.WorkerId
        };
    }

    private sealed class ProxyTcpRequestDto
    {
        public string Host { get; init; } = string.Empty;
        public int Port { get; init; }
        public string Payload { get; init; } = string.Empty;
        public TimeSpan Timeout { get; init; }
        public string Purpose { get; init; } = string.Empty;
    }

    private sealed class ProxyTcpResponseDto
    {
        public bool Success { get; init; }
        public string? ResponseText { get; init; }
        public string? ErrorMessage { get; init; }
        public long ElapsedMs { get; init; }
        public string? ProxyId { get; init; }
        public int? WorkerId { get; init; }
    }
}
