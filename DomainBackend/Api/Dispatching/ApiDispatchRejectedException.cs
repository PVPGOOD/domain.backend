using System;

namespace Domain.Backend.Api.Dispatching;

public sealed class ApiDispatchRejectedException(string message) : Exception(message);
