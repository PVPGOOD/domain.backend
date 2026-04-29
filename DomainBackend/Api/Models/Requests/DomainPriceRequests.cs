using System.Collections.Generic;

namespace Domain.Backend.Api.Models.Requests;

public sealed record QueryRegistrationPricesRequest(IReadOnlyList<string> Tlds);
