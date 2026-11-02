namespace Rahgir.AspNetCore;

/// <summary>Gives an endpoint a human-readable action name in the dashboard, e.g. [TrackAs("ثبت سفارش")].</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TrackAsAttribute : Attribute
{
    public TrackAsAttribute(string name) => Name = name;
    public string Name { get; }
}

/// <summary>Never track this controller / action.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class NoTrackAttribute : Attribute { }
