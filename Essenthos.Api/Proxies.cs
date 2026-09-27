using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace Essenthos.Core;

/// <summary>
/// The proxies whose <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> are believed, from
/// <c>ForwardedHeaders:KnownProxies</c>: addresses, or networks written as <c>10.89.0.0/24</c>.
///
/// Named rather than trusted wholesale, because the address a request is limited by is the one this
/// header gives: a client allowed to write it could be anybody it liked. Nothing configured means
/// nothing forwarded is believed, which is right for a development machine with no proxy in front.
/// </summary>
internal static class Proxies
{
    public const string ConfigurationKey = "ForwardedHeaders:KnownProxies";

    public static ForwardedHeadersOptions? Read(IConfiguration configuration)
    {
        var entries = configuration.GetSection(ConfigurationKey).GetChildren()
            .Select(child => child.Value)
            .Append(configuration[ConfigurationKey])
            .SelectMany(value => (value ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        if (entries.Count == 0)
        {
            return null;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var entry in entries)
        {
            if (entry.Contains('/') && IPNetwork.TryParse(entry, out var network))
            {
                options.KnownIPNetworks.Add(network);
            }
            else if (!entry.Contains('/') && IPAddress.TryParse(entry, out var address))
            {
                options.KnownProxies.Add(address);
            }
            else
            {
                throw new InvalidOperationException(
                    $"{ConfigurationKey} holds \"{entry}\", which is neither an address nor a network. Write the " +
                    "proxy's address, such as 10.89.0.2, or its network, such as 10.89.0.0/24.");
            }
        }

        return options;
    }

    public static IApplicationBuilder UseProxies(this IApplicationBuilder app, ForwardedHeadersOptions? options) =>
        options is null ? app : app.UseForwardedHeaders(options);
}
