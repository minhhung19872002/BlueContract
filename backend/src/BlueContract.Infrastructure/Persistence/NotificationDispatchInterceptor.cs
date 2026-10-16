using BlueContract.Application.Notifications;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BlueContract.Infrastructure.Persistence;

/// <summary>Sends the queued realtime pushes and emails only after the transaction committed; drops them on failure.</summary>
internal sealed class NotificationDispatchInterceptor(NotificationOutbox outbox) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (!outbox.IsEmpty)
        {
            await outbox.FlushAsync(cancellationToken);
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (!outbox.IsEmpty)
        {
            outbox.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        outbox.Clear();
        return Task.CompletedTask;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => outbox.Clear();
}
