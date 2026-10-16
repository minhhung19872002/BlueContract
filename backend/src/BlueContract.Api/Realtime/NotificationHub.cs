using BlueContract.Application.Notifications;
using BlueContract.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BlueContract.Api.Realtime;

/// <summary>Realtime channel (spec §9.3): "notification" events when a document waits for the user, etc.</summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    public const string Path = "/hubs/notifications";
}

/// <summary>Maps SignalR connections to our user id (JWT "sub").</summary>
internal sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirst(AppClaims.UserId)?.Value;
}

internal sealed class SignalRNotifier(IHubContext<NotificationHub> hub) : IRealtimeNotifier
{
    public Task NotifyUsersAsync(IReadOnlyCollection<Guid> userIds, RealtimeMessage message, CancellationToken ct) =>
        hub.Clients.Users(userIds.Select(id => id.ToString()).ToList()).SendAsync("notification", message, ct);
}

/// <summary>Hangfire dashboard access: development environment only (production keeps it disabled).</summary>
internal sealed class DevelopmentDashboardAuthorization(IWebHostEnvironment environment) : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context) => environment.IsDevelopment();
}
