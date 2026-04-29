using System;
using Domain.Backend.Tasks.Models.Enums;

namespace Domain.Backend.Tasks.Whois;

public interface IWhoisResponseParser
{
    DomainAvailability Parse(string responseText);
    DateTimeOffset? ExtractExpirationDate(string responseText);
}
