using Essenthos.Core.Corpus;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the record and occupation routes read stored answers and validators with, registered as the API registers it, for a
/// test that maps those routes on an app of its own. A service the test registered first is kept.
/// </summary>
internal static class PageServices
{
    public static IServiceCollection AddPageServices(this IServiceCollection services)
    {
        services.TryAddSingleton(new SiteSettingsFile(
            Path.Combine(Path.GetTempPath(), $"essenthos-no-settings-{Guid.NewGuid():N}.json"), NullLogger<SiteSettingsFile>.Instance));
        services.TryAddSingleton<ICanonIndex, CanonIndex>();
        services.TryAddSingleton(new DownloadsCatalogue(null, null, NullLogger<DownloadsCatalogue>.Instance));
        services.TryAddSingleton<PageCache>();
        services.TryAddSingleton<PageValidator>();
        services.TryAddScoped<PageLoader>();
        return services;
    }
}
