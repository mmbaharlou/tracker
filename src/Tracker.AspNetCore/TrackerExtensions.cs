using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracker.Core;

namespace Tracker.AspNetCore;

public static class TrackerExtensions
{
    /// <summary>Registers Tracker using the "Tracker" configuration section, then applies <paramref name="configure"/>.</summary>
    public static IServiceCollection AddTracker(this IServiceCollection services, IConfiguration configuration, Action<TrackerOptions>? configure = null)
    {
        var options = new TrackerOptions();
        configuration.GetSection("Tracker").Bind(options);
        configure?.Invoke(options);
        return services.AddTracker(options);
    }

    public static IServiceCollection AddTracker(this IServiceCollection services, Action<TrackerOptions> configure)
    {
        var options = new TrackerOptions();
        configure(options);
        return services.AddTracker(options);
    }

    private static IServiceCollection AddTracker(this IServiceCollection services, TrackerOptions options)
    {
        services.TryAddSingleton(options);
        services.TryAddSingleton(new Masker(options.SensitiveKeys));
        services.TryAddSingleton<EventQueue>();
        services.TryAddSingleton<EffectiveSettings>();
        services.AddHttpContextAccessor();
        services.TryAddSingleton<ITracker, DefaultTracker>();
        services.AddHttpClient(BatchSender.HttpClientName, c => c.Timeout = options.HttpTimeout);
        services.AddHostedService<BatchSender>();
        services.AddHostedService<RemoteSettingsPoller>();
        return services;
    }

    /// <summary>Adds the tracking middleware. Place it early, e.g. right after UseRouting().</summary>
    public static IApplicationBuilder UseTracker(this IApplicationBuilder app)
        => app.UseMiddleware<TrackerMiddleware>();

    /// <summary>Minimal API: name an endpoint for the dashboard.</summary>
    public static TBuilder TrackAs<TBuilder>(this TBuilder builder, string name) where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(new TrackAsAttribute(name));

    /// <summary>Minimal API: exclude an endpoint from tracking.</summary>
    public static TBuilder NoTrack<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(new NoTrackAttribute());
}
