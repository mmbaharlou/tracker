using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rahgir.Core;

namespace Rahgir.AspNetCore;

public static class RahgirExtensions
{
    /// <summary>Registers Rahgir using the "Rahgir" configuration section, then applies <paramref name="configure"/>.</summary>
    public static IServiceCollection AddRahgir(this IServiceCollection services, IConfiguration configuration, Action<RahgirOptions>? configure = null)
    {
        var options = new RahgirOptions();
        configuration.GetSection("Rahgir").Bind(options);
        configure?.Invoke(options);
        return services.AddRahgir(options);
    }

    public static IServiceCollection AddRahgir(this IServiceCollection services, Action<RahgirOptions> configure)
    {
        var options = new RahgirOptions();
        configure(options);
        return services.AddRahgir(options);
    }

    private static IServiceCollection AddRahgir(this IServiceCollection services, RahgirOptions options)
    {
        services.TryAddSingleton(options);
        services.TryAddSingleton(new Masker(options.SensitiveKeys));
        services.TryAddSingleton<EventQueue>();
        services.TryAddSingleton<EffectiveSettings>();
        services.AddHttpContextAccessor();
        services.TryAddSingleton<IRahgirTracker, RahgirTracker>();
        services.AddHttpClient(BatchSender.HttpClientName, c => c.Timeout = options.HttpTimeout);
        services.AddHostedService<BatchSender>();
        services.AddHostedService<RemoteSettingsPoller>();
        return services;
    }

    /// <summary>Adds the tracking middleware. Place it early, e.g. right after UseRouting().</summary>
    public static IApplicationBuilder UseRahgir(this IApplicationBuilder app)
        => app.UseMiddleware<RahgirMiddleware>();

    /// <summary>Minimal API: name an endpoint for the dashboard.</summary>
    public static TBuilder TrackAs<TBuilder>(this TBuilder builder, string name) where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(new TrackAsAttribute(name));

    /// <summary>Minimal API: exclude an endpoint from tracking.</summary>
    public static TBuilder NoTrack<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(new NoTrackAttribute());
}
