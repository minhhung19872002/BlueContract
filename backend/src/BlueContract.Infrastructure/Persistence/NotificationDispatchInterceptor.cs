using System.Data.Common;
using BlueContract.Application.Notifications;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace BlueContract.Infrastructure.Persistence;

/// <summary>
/// After a successful commit: sends the queued realtime pushes / emails and runs post-commit work (each item in its
/// own DI scope). Inside an explicit transaction (<see cref="AppDbContext.SerializedAsync{T}"/>) SaveChanges is not a
/// commit yet, so dispatch waits for the transaction to commit. On failure or rollback everything queued is dropped.
/// </summary>
internal sealed class NotificationDispatchInterceptor(NotificationOutbox outbox, PostCommitWork postCommit, IServiceScopeFactory scopes)
    : SaveChangesInterceptor, IDbTransactionInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.Database.CurrentTransaction is null)
        {
            await DispatchAsync(cancellationToken);
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) =>
        SavedChangesAsync(eventData, result).AsTask().GetAwaiter().GetResult();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Drop();
        return Task.CompletedTask;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Drop();

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        DispatchAsync(cancellationToken);

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        DispatchAsync(CancellationToken.None).GetAwaiter().GetResult();

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Drop();
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Drop();

    private async Task DispatchAsync(CancellationToken cancellationToken)
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
    }

    private void Drop()
    {
        outbox.Clear();
        postCommit.Clear();
    }
}
