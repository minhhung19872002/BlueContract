using BlueContract.Application.Notifications;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace BlueContract.Infrastructure.Persistence;

/// <summary>
/// After a successful SaveChanges: sends the queued realtime pushes / emails and runs post-commit work (each item in its
/// own DI scope). On failure everything queued is dropped.
/// </summary>
internal sealed class NotificationDispatchInterceptor(NotificationOutbox outbox, PostCommitWork postCommit, IServiceScopeFactory scopes) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (!outbox.IsEmpty)
        {
            await outbox.FlushAsync(cancellationToken);
        }

        foreach (var work in postCommit.TakeAll())
        {
            using var scope = scopes.CreateScope();
            await work(scope.ServiceProvider, cancellationToken);
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) =>
        SavedChangesAsync(eventData, result).AsTask().GetAwaiter().GetResult();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        outbox.Clear();
        postCommit.Clear();
        return Task.CompletedTask;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        outbox.Clear();
        postCommit.Clear();
    }
}
