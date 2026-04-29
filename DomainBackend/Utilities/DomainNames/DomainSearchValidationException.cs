using System;

namespace Domain.Backend.Utilities.DomainNames;

public sealed class DomainSearchValidationException(string message, string? field = null) : Exception(message)
{
    public string? Field { get; } = field;
}
