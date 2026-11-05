using Microsoft.AspNetCore.Mvc;
using Tracker.Api.Auth;

namespace Tracker.Api.Controllers;

public static class ControllerExtensions
{
    public static PanelUser Me(this ControllerBase c) => c.HttpContext.PanelUser() ?? throw new UnauthorizedAccessException();

    public static string? ClientIp(this ControllerBase c) => c.HttpContext.Connection.RemoteIpAddress?.ToString();

    public static ObjectResult Fail(this ControllerBase c, int status, string message) => c.StatusCode(status, new { message });

    /// <summary>403 unless the signed-in user may see this project.</summary>
    public static ObjectResult? DenyProject(this ControllerBase c, string? projectKey)
        => c.HttpContext.PanelUser()?.CanSee(projectKey) == true ? null : c.Fail(403, "به این پروژه دسترسی ندارید.");
}

public sealed record PageResult<T>(List<T> Items, int Total, int Page, int PageSize);
