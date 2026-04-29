using System;
using System.IO;
using System.Text.Json.Serialization;
using Domain.Backend.Api.Dispatching;
using Domain.Backend.Sql;
using Domain.Backend.Tasks;
using Domain.Backend.Tasks.Search;
using Domain.Backend.Tasks.Whois;
using Domain.Backend.Utilities.DomainNames;
using Domain.Backend.Utilities.Pricing;
using Domain.Backend.Utilities.Proxy;
using Domain.Backend.Utilities.Rdap;
using Domain.Backend.Utilities.Whois;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=App_Data/domain_backend.db";

builder.Services.AddDbContext<DomainBackendDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<IDomainSearchTaskService, DomainSearchTaskService>();
builder.Services.AddSingleton<IApiDispatchConstraintProvider, DefaultApiDispatchConstraintProvider>();
builder.Services.AddSingleton<IApiDispatcher, ApiDispatcher>();
builder.Services.AddSingleton<IDomainNameNormalizer, DomainNameNormalizer>();
builder.Services.AddSingleton<IDomainSearchHandler, ExactDomainSearchHandler>();
builder.Services.AddSingleton<IDomainSearchHandler, KeywordDomainSearchHandler>();
builder.Services.AddSingleton<IDomainSearchHandler, ShortBatchDomainSearchHandler>();
builder.Services.AddSingleton<IWhoisServerResolver, StaticWhoisServerResolver>();
builder.Services.AddSingleton<IWhoisResponseParser, WhoisResponseParser>();
builder.Services.AddHttpClient<IDomainRdapLookupService, IanaRdapLookupService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(12);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DomainBackend/1.0");
});
builder.Services.AddHttpClient<ICurrencyExchangeRateService, FrankfurterCurrencyExchangeRateService>(client =>
{
    client.BaseAddress = new Uri("https://api.frankfurter.dev/");
    client.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddHttpClient<IDomainRegistrationPriceService, NazhumiDomainRegistrationPriceService>(client =>
{
    client.BaseAddress = new Uri("https://www.nazhumi.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<IDomainAvailabilityProvider, WhoisDomainAvailabilityProvider>();
builder.Services.AddHttpClient<IOutboundTcpRequestDispatcher, HttpProxyManagerOutboundTcpRequestDispatcher>(client =>
{
    var proxyManagerBaseUrl = builder.Configuration["ProxyManager:BaseUrl"]
        ?? "http://127.0.0.1:5010/";
    client.BaseAddress = new Uri(proxyManagerBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHostedService<DomainSearchWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DomainBackendDbContext>();
    Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
    await db.Database.EnsureCreatedAsync();
    await SqliteSchemaMaintenance.EnsureAsync(db);
}

app.MapControllers();

await app.RunAsync();
