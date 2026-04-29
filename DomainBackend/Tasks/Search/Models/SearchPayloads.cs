namespace Domain.Backend.Tasks.Search.Models;

internal sealed class ExactPayload
{
    public string[]? Domains { get; set; }
}

internal sealed class KeywordPayload
{
    public string[]? Keywords { get; set; }
}

internal sealed class ShortBatchPayload
{
    public int Length { get; set; }
    public string? Charset { get; set; }
}
