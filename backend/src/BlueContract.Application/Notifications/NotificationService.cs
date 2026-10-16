using System.Net;
using BlueContract.Application.Common;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Notifications;

/// <summary>Pushes realtime events to connected browsers (SignalR in the API).</summary>
public interface IRealtimeNotifier
{
    Task NotifyUsersAsync(IReadOnlyCollection<Guid> userIds, RealtimeMessage message, CancellationToken ct);
}

public sealed record RealtimeMessage(string Type, string Title, string? Body, string? Link);

/// <summary>Durable background work (Hangfire): emails are sent outside the request.</summary>
public interface IBackgroundJobs
{
    void EnqueueEmail(EmailMessage message);
}

/// <summary>
/// Realtime pushes and emails wait here until the unit of work commits, so browsers that refetch on a push read the new
/// state and no email goes out for a change that was rolled back. Flushed by the persistence layer after SaveChanges.
/// </summary>
public sealed class NotificationOutbox(IRealtimeNotifier realtime, IBackgroundJobs jobs)
{
    private readonly List<(IReadOnlyCollection<Guid> Users, RealtimeMessage Message)> _pushes = [];
    private readonly List<EmailMessage> _emails = [];

    public bool IsEmpty => _pushes.Count == 0 && _emails.Count == 0;

    public void Queue(IReadOnlyCollection<Guid> users, RealtimeMessage message) => _pushes.Add((users, message));

    public void Queue(EmailMessage email) => _emails.Add(email);

    public async Task FlushAsync(CancellationToken ct)
    {
        var pushes = _pushes.ToList();
        var emails = _emails.ToList();
        Clear();
        foreach (var email in emails)
        {
            jobs.EnqueueEmail(email);
        }

        foreach (var (users, message) in pushes)
        {
            await realtime.NotifyUsersAsync(users, message, ct);
        }
    }

    public void Clear()
    {
        _pushes.Clear();
        _emails.Clear();
    }
}

/// <summary>
/// Work that must run once the current unit of work has committed, in a fresh DI scope (its own DbContext sees the
/// committed data). Executed by the persistence layer right after SaveChanges succeeds.
/// </summary>
public sealed class PostCommitWork
{
    private readonly List<Func<IServiceProvider, CancellationToken, Task>> _work = [];

    public bool IsEmpty => _work.Count == 0;

    public void Enqueue(Func<IServiceProvider, CancellationToken, Task> work) => _work.Add(work);

    public IReadOnlyList<Func<IServiceProvider, CancellationToken, Task>> TakeAll()
    {
        var all = _work.ToList();
        _work.Clear();
        return all;
    }

    public void Clear() => _work.Clear();
}

public sealed record NotificationDto(Guid Id, string Type, string Title, string? Body, string? Link, DateTimeOffset CreatedAt, bool IsRead);

public sealed record NotificationListDto(IReadOnlyList<NotificationDto> Items, int UnreadCount);

/// <summary>In-app notification + realtime push + email (spec §5.7, §6.3).</summary>
public sealed class NotificationService(
    IAppDbContext db,
    NotificationOutbox outbox,
    IAppLinkBuilder links,
    ICurrentUser currentUser,
    IClock clock)
{
    /// <summary>Adds notifications to the unit of work; push + email go out once the caller's SaveChanges commits.</summary>
    public async Task NotifyAsync(IEnumerable<Guid> userIds, string type, string title, string? body, string? link, bool email, CancellationToken ct)
    {
        var targets = userIds.Distinct().ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var userId in targets)
        {
            db.Notifications.Add(new Notification(userId, type, title, body, link, now));
        }

        outbox.Queue(targets, new RealtimeMessage(type, title, body, link));

        if (!email)
        {
            return;
        }

        var recipients = await db.Users.IgnoreQueryFilters()
            .Where(u => targets.Contains(u.Id) && u.Email != null && u.Status == Domain.Identity.UserStatus.Active)
            .Select(u => new { u.Email, u.FullName })
            .ToListAsync(ct);
        foreach (var r in recipients)
        {
            var url = link is null ? null : links.Build(link);
            var html = $"""
                <p>Xin chào {WebUtility.HtmlEncode(r.FullName)},</p>
                <p><b>{WebUtility.HtmlEncode(title)}</b></p>
                {(body is null ? string.Empty : $"<p>{WebUtility.HtmlEncode(body)}</p>")}
                {(url is null ? string.Empty : $"<p><a href=\"{url}\">Mở trên Cổng mua sắm trực tuyến</a></p>")}
                <p style="color:#888">Email tự động, vui lòng không trả lời.</p>
                """;
            outbox.Queue(new EmailMessage([r.Email!], title, html));
        }
    }

    public async Task<NotificationListDto> ListMineAsync(bool unreadOnly, int take, CancellationToken ct)
    {
        var me = currentUser.UserId!.Value;
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == me);
        var unread = await query.CountAsync(n => n.ReadAt == null, ct);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var items = await query.OrderByDescending(n => n.CreatedAt).Take(Math.Clamp(take, 1, 100))
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt != null))
            .ToListAsync(ct);
        return new NotificationListDto(items, unread);
    }

    public async Task MarkReadAsync(Guid? id, CancellationToken ct)
    {
        var me = currentUser.UserId!.Value;
        var query = db.Notifications.Where(n => n.UserId == me && n.ReadAt == null);
        if (id is not null)
        {
            query = query.Where(n => n.Id == id);
        }

        var now = clock.UtcNow;
        foreach (var n in await query.ToListAsync(ct))
        {
            n.MarkRead(now);
        }

        await db.SaveChangesAsync(ct);
    }
}

public static class NotificationTypes
{
    public const string SigningRequired = "signing.required";
    public const string WorkflowReturned = "workflow.returned";
    public const string WorkflowCompleted = "workflow.completed";
    public const string WorkflowOverdue = "workflow.overdue";
    public const string WorkflowComment = "workflow.comment";
    public const string ContractEffective = "contract.effective";
    public const string ProposalSubmitted = "proposal.submitted";
    public const string SummaryApproved = "summary.approved";
    public const string CallOrderSent = "callorder.sent";
    public const string CallOrderConfirmed = "callorder.confirmed";
    public const string ReceiptCompleted = "receipt.completed";
    public const string ReceiptReturned = "receipt.returned";
    public const string ReceiptInvoice = "receipt.invoice";
    public const string AcceptanceDrafted = "acceptance.drafted";
    public const string PaymentCompleted = "payment.completed";
    public const string ContractLiquidated = "contract.liquidated";
}
