using BlueContract.Application.Common;
using BlueContract.Application.Notifications;
using Shouldly;

namespace BlueContract.Application.Tests;

public class NotificationOutboxTests
{
    private sealed class Recorder : IRealtimeNotifier, IBackgroundJobs
    {
        public List<RealtimeMessage> Pushes { get; } = [];

        public List<EmailMessage> Emails { get; } = [];

        public Task NotifyUsersAsync(IReadOnlyCollection<Guid> userIds, RealtimeMessage message, CancellationToken ct)
        {
            Pushes.Add(message);
            return Task.CompletedTask;
        }

        public void EnqueueEmail(EmailMessage message) => Emails.Add(message);
    }

    [Fact]
    public async Task Nothing_is_sent_before_flush_and_flush_sends_once()
    {
        var recorder = new Recorder();
        var outbox = new NotificationOutbox(recorder, recorder);
        outbox.Queue([Guid.NewGuid()], new RealtimeMessage("signing.required", "Cần ký", null, "/trinh-ky"));
        outbox.Queue(new EmailMessage(["a@example.vn"], "Cần ký", "<p>x</p>"));

        recorder.Pushes.ShouldBeEmpty();
        recorder.Emails.ShouldBeEmpty();

        await outbox.FlushAsync(CancellationToken.None);
        await outbox.FlushAsync(CancellationToken.None);

        recorder.Pushes.Count.ShouldBe(1);
        recorder.Emails.Count.ShouldBe(1);
        outbox.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task Clear_drops_messages_of_a_failed_unit_of_work()
    {
        var recorder = new Recorder();
        var outbox = new NotificationOutbox(recorder, recorder);
        outbox.Queue([Guid.NewGuid()], new RealtimeMessage("workflow.returned", "Trả lại", null, null));

        outbox.Clear();
        await outbox.FlushAsync(CancellationToken.None);

        recorder.Pushes.ShouldBeEmpty();
    }
}
