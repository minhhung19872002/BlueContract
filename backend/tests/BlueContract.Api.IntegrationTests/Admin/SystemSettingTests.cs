using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Admin;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Workflows;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Admin;

/// <summary>
/// AUDIT-1 §11.13 / spec §11 "mọi ngưỡng … trong SystemSetting sửa được trên UI": job schedules are settings and a change
/// re-registers the job at once; values are checked against their range; the default SLA is really used.
/// </summary>
[Collection(ApiCollection.Name)]
public class SystemSettingTests(ApiFactory factory)
{
    private const string Settings = "/api/admin/settings";

    [Fact]
    public async Task Changing_a_schedule_reregisters_the_job_immediately()
    {
        using var admin = await factory.LoginAsync("admin");
        var keys = (await admin.GetFromJsonAsync<JsonElement>(Settings)).EnumerateArray().Select(s => s.GetProperty("key").GetString()).ToList();
        keys.ShouldContain("job.acceptance_hour");
        keys.ShouldContain("job.integrity_check_hour");
        keys.ShouldContain("job.workflow_reminder_minutes");

        try
        {
            await PutAsync(admin, "job.acceptance_hour", "5");
            await PutAsync(admin, "job.integrity_check_hour", "23");
            await PutAsync(admin, "job.workflow_reminder_minutes", "15");

            var jobs = Recurring();
            jobs.Single(j => j.Id == "monthly-acceptance").Cron.ShouldBe("0 5 * * *");
            jobs.Single(j => j.Id == "integrity-check").Cron.ShouldBe("0 23 * * *");
            jobs.Single(j => j.Id == "workflow-sla-reminders").Cron.ShouldBe("*/15 * * * *");
        }
        finally
        {
            await PutAsync(admin, "job.acceptance_hour", "1");
            await PutAsync(admin, "job.integrity_check_hour", "2");
            await PutAsync(admin, "job.workflow_reminder_minutes", "60");
        }

        Recurring().Single(j => j.Id == "workflow-sla-reminders").Cron.ShouldBe("0 * * * *");
    }

    [Fact]
    public async Task Values_outside_their_range_are_refused_with_the_range()
    {
        using var admin = await factory.LoginAsync("admin");
        var day = (await admin.GetFromJsonAsync<JsonElement>(Settings)).EnumerateArray().Single(s => s.GetProperty("key").GetString() == "acceptance.job_day_of_month");
        day.GetProperty("min").GetDecimal().ShouldBe(1);
        day.GetProperty("max").GetDecimal().ShouldBe(28);

        using var response = await admin.PutAsJsonAsync($"{Settings}/acceptance.job_day_of_month", new { value = "31" });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("SETTING_VALUE_OUT_OF_RANGE");
        problem.GetProperty("title").GetString()!.ShouldContain("28");

        using var hour = await admin.PutAsJsonAsync($"{Settings}/job.acceptance_hour", new { value = "24" });
        hour.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Steps_without_their_own_sla_use_the_default_sla_setting()
    {
        using var admin = await factory.LoginAsync("admin");
        var code = "WF_NOSLA_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using (var created = await admin.PostAsJsonAsync("/api/workflow-templates", new SaveWorkflowTemplateRequest(code, "Không đặt SLA", DocumentType.TemplateSample, null, false, true,
            [new("CHECK", "Thủ kho kiểm tra", WorkflowActionType.Approve, AssigneeType.Role, "STOREKEEPER", null, false, false, [0], null, null)]), JsonDefaults.Options))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        }

        try
        {
            await PutAsync(admin, "workflow.default_sla_hours", "5");
            var templates = await admin.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates", JsonDefaults.Options);
            using var doc = await admin.PostAsJsonAsync("/api/documents/samples", new CreateSampleDocumentRequest(templates!.Single(t => t.Code == "PHIEU_NHAP_KHO").Id, null));
            var docId = await doc.Content.ReadFromJsonAsync<Guid>();
            var workflowTemplates = await admin.GetFromJsonAsync<List<WorkflowTemplateDto>>("/api/workflow-templates", JsonDefaults.Options);
            using var start = await admin.PostAsJsonAsync("/api/workflows",
                new StartWorkflowRequest(docId, workflowTemplates!.Single(t => t.Code == code).Id, null, null, null), JsonDefaults.Options);
            start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

            var wf = await admin.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{docId}", JsonDefaults.Options);
            var step = wf!.Steps.Single();
            step.DueAt.ShouldNotBeNull();
            (step.DueAt!.Value - step.ActivatedAt!.Value).ShouldBe(TimeSpan.FromHours(5));
        }
        finally
        {
            await PutAsync(admin, "workflow.default_sla_hours", "24");
        }
    }

    private List<RecurringJobDto> Recurring()
    {
        using var connection = factory.Services.GetRequiredService<JobStorage>().GetConnection();
        return connection.GetRecurringJobs();
    }

    private static async Task PutAsync(HttpClient admin, string key, string value)
    {
        using var response = await admin.PutAsJsonAsync($"{Settings}/{key}", new UpdateSettingRequest(value), JsonDefaults.Options);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{key}={value}: " + await response.Content.ReadAsStringAsync());
    }
}
