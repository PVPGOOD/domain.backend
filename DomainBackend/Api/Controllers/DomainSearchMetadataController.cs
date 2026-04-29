using System.Collections.Generic;
using System.Linq;
using Domain.Backend;
using Domain.Backend.Tasks.Whois;
using Microsoft.AspNetCore.Mvc;

namespace Domain.Backend.Api.Controllers;

[ApiController]
[Route("api/v1/domain-search")]
public sealed class DomainSearchMetadataController(IWhoisServerResolver whoisServerResolver) : ControllerBase
{
    [HttpPost("supported-tlds")]
    public ActionResult<SupportedTldsResponse> GetSupportedTlds()
    {
        var items = whoisServerResolver.GetSupportedTldInfos()
            .Select(tld => new SupportedTldResponse("." + tld.Tld, tld.Category))
            .ToArray();

        return Ok(new SupportedTldsResponse(items.Select(static item => item.Tld).ToArray(), items));
    }

    [HttpPost("build-info")]
    public ActionResult<BuildInfoResponse> GetBuildInfo()
    {
        return Ok(new BuildInfoResponse(BuildInfo.GitCommit, BuildInfo.BuildTimeUtc));
    }
}

public sealed record SupportedTldsResponse(IReadOnlyList<string> Tlds, IReadOnlyList<SupportedTldResponse> Items);

public sealed record SupportedTldResponse(string Tld, string Category);

public sealed record BuildInfoResponse(string GitCommit, string BuildTimeUtc);
