using System;

namespace Domain.Backend.Utilities.Proxy.Models;

public sealed class OutboundTcpRequest
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Payload { get; init; } = string.Empty;
    public TimeSpan Timeout { get; init; }
    public string Purpose { get; init; } = string.Empty;
}

public sealed class OutboundTcpResponse
{
    public bool Success { get; init; }
    public string? ResponseText { get; init; }
    public string? ErrorMessage { get; init; }
    public long? ElapsedMs { get; init; }
    public string? ProxyId { get; init; }
    public int? WorkerId { get; init; }
}
