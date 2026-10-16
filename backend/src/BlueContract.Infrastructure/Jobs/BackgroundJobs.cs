using BlueContract.Application.Common;
using BlueContract.Application.Notifications;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Workflows;
using BlueContract.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlueContract.Infrastructure.Jobs;

public sealed class HangfireOptions
{
    public const string SectionName = "Hangfire";

    /// <summary>Run the job server in this process (disable on extra API replicas or in tests).</summary>
    public bool ServerEnabled { get; set; } = true;

    /// <summary>Expose /hangfire (development only by default).</summary>
    public bool DashboardEnabled { get; set; }
}

/// <summary>Durable jobs on Hangfire (PostgreSQL storage): email delivery with automatic retries.</summary>
internal sealed class HangfireBackgroundJobs(IBackgroundJobClient client) : IBackgroundJobs
{
    public void EnqueueEmail(EmailMessage message)
    {
        // Job arguments are stored as JSON: use plain arrays (collection expressions may produce compiler-generated list
        // types that cannot be deserialized by the worker).
        var payload = new EmailMessage(message.To.ToArray(), message.Subject, message.HtmlBody, message.Cc?.ToArray());
        client.Enqueue<EmailJob>(job => job.SendAsync(payload, CancellationToken.None));
    }
}

public sealed class EmailJob(IEmailSender sender)
{
    [AutomaticRetry(Attempts = 5)]
    public Task SendAsync(EmailMessage message, CancellationToken ct) => sender.SendAsync(message, ct);
}

/// <summary>Hourly SLA check: reminds assignees of steps past their due time (spec §5.7 "SLA để nhắc việc").</summary>
public sealed class WorkflowReminderJob(AppDbContext db, WorkflowAccess access, NotificationService notifications, IClock clock, ILogger<WorkflowReminderJob> logger)
{
    public const string Id = "workflow-sla-reminders";

    public async Task RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var overdue = await db.WorkflowSteps
            .Where(s => s.Status == StepStatus.Active && s.DueAt != null && s.DueAt < now && s.ReminderSentAt == null)
            .Select(s => s.InstanceId).Distinct().ToListAsync(ct);
        foreach (var instanceId in overdue)
        {
            var wf = await db.WorkflowInstances.IgnoreQueryFilters().Include("_steps").FirstAsync(w => w.Id == instanceId, ct);
            if (wf.ActiveStep is not { } step)
            {
                continue;
            }

            var title = await db.Documents.IgnoreQueryFilters().Where(d => d.Id == wf.DocumentId).Select(d => d.Title).FirstAsync(ct);
            await notifications.NotifyAsync(await access.AssigneesAsync(wf, step, ct), NotificationTypes.WorkflowOverdue,
                $"Quá hạn xử lý: {title}", $"Bước \"{step.Name}\" đã quá hạn từ {step.DueAt:dd/MM/yyyy HH:mm} (UTC).", $"/trinh-ky?document={wf.DocumentId}", email: true, ct);
            step.MarkReminderSent(now);
        }

        await db.SaveChangesAsync(ct);
        if (overdue.Count > 0)
        {
            logger.LogInformation("Sent SLA reminders for {Count} workflows", overdue.Count);
        }
    }
}

/// <summary>Periodic pull of warehouse receipts from the HIS (spec §6.4 B1 "pull định kỳ", His:PullEnabled / His:PullCron).</summary>
public sealed class HisReceiptPullJob(BlueContract.Application.Integration.IntegrationLogService integration, Microsoft.Extensions.Options.IOptions<BlueContract.Infrastructure.Integration.HisOptions> options, ILogger<HisReceiptPullJob> logger)
{
    public const string Id = "his-receipt-pull";

    [DisableConcurrentExecution(600)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken ct)
    {
        var result = await integration.PullAsync(options.Value.PullLookbackDays, ct);
        if (result.Received > 0 || result.Failed > 0)
        {
            logger.LogInformation("HIS pull: {Received} receipts, {Created} created, {Updated} updated, {Failed} failed", result.Received, result.Created, result.Updated, result.Failed);
        }
    }
}

/// <summary>
/// Spec §6.5: on the configured day of each month (SystemSetting acceptance.job_day_of_month, default 1), drafts one
/// acceptance per contract from the signed receipts not yet accepted. Runs daily at 01:00 Vietnam time and checks the day.
/// </summary>
public sealed class AcceptanceJob(BlueContract.Application.Payments.AcceptanceService acceptances, BlueContract.Application.Admin.SettingService settings, IClock clock, ILogger<AcceptanceJob> logger)
{
    public const string Id = "monthly-acceptance";

    [DisableConcurrentExecution(1800)]
    [AutomaticRetry(Attempts = 2)]
    public async Task RunAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var day = Math.Clamp(await settings.GetIntAsync(BlueContract.Domain.Settings.SystemSettingKeys.AcceptanceJobDayOfMonth, 1, ct), 1, 28);
        if (today.Day != day)
        {
            return;
        }

        var result = await acceptances.GenerateMonthlyAsync(today, ct);
        logger.LogInformation("Monthly acceptance job: {Count} drafts for {Period}", result.Created, result.PeriodText);
    }
}

/// <summary>Spec §11: daily re-hash of every stored file against the SHA-256 kept in the database (02:00 Vietnam time).</summary>
public sealed class IntegrityCheckJob(BlueContract.Application.Admin.IntegrityService integrity)
{
    public const string Id = "integrity-check";

    [DisableConcurrentExecution(3600)]
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(CancellationToken ct) => integrity.RunAsync("Lịch định kỳ", ct);
}
