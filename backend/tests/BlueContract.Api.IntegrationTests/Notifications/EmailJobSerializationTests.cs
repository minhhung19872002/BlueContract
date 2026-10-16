using BlueContract.Application.Common;
using BlueContract.Infrastructure.Jobs;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Notifications;

/// <summary>Email jobs are persisted as JSON by Hangfire and must round-trip, whatever list type the caller used.</summary>
public class EmailJobSerializationTests
{
    private sealed class CapturingClient : IBackgroundJobClient
    {
        public Job? Job { get; private set; }

        public string Create(Job job, IState state)
        {
            Job = job;
            return "1";
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    [Fact]
    public void Enqueued_email_round_trips_through_the_job_serializer()
    {
        GlobalConfiguration.Configuration.UseRecommendedSerializerSettings();
        var client = new CapturingClient();

        // Collection expressions produce compiler-generated list types (the bug this test guards against).
        new HangfireBackgroundJobs(client).EnqueueEmail(new EmailMessage(["a@example.vn"], "Tiêu đề", "<p>Nội dung</p>", ["b@example.vn"]));

        var argument = client.Job!.Args[0];
        var json = SerializationHelper.Serialize(argument, SerializationOption.User);
        var restored = SerializationHelper.Deserialize<EmailMessage>(json, SerializationOption.User);

        restored.To.ShouldBe(["a@example.vn"]);
        restored.Cc.ShouldBe(["b@example.vn"]);
        restored.Subject.ShouldBe("Tiêu đề");
    }
}
