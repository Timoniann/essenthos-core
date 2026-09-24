using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

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
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var entry in entries)
        {
            var parts = entry.Split('/');
            if (!IPAddress.TryParse(parts[0], out var address) ||
                parts.Length > 2 ||
                (parts.Length == 2 && !int.TryParse(parts[1], out _)))
            {
                throw new InvalidOperationException(
                    $"{ConfigurationKey} holds \"{entry}\", which is neither an address nor a network. Write the " +
                    "proxy's address, such as 10.89.0.2, or its network, such as 10.89.0.0/24.");
            }

            if (parts.Length == 2)
            {
                options.KnownNetworks.Add(new IPNetwork(address, int.Parse(parts[1])));
            }
            else
            {
                options.KnownProxies.Add(address);
            }
        }

        return options;
    }

    public static IApplicationBuilder UseProxies(this IApplicationBuilder app, ForwardedHeadersOptions? options) =>
        options is null ? app : app.UseForwardedHeaders(options);
}
