using BlueContract.Domain.Common;

namespace BlueContract.Domain.Settings;

/// <summary>
/// Admin-edited email of one event (spec §3 "template email theo sự kiện"). Events without a row use the built-in
/// template; removing the row restores it.
/// </summary>
public sealed class EmailTemplate : Entity
{
    private EmailTemplate()
    {
        EventType = string.Empty;
        Subject = string.Empty;
        BodyHtml = string.Empty;
    }

    public EmailTemplate(string eventType, string subject, string bodyHtml)
    {
        EventType = Guard.Required(eventType, "EMAIL_TEMPLATE_EVENT_REQUIRED");
        Subject = string.Empty;
        BodyHtml = string.Empty;
        Update(subject, bodyHtml);
    }

    /// <summary>Notification type (<c>signing.required</c>…) or account email (<c>account.created</c>…).</summary>
    public string EventType { get; private set; }

    /// <summary>Plain-text subject with <c>{{Placeholder}}</c>s.</summary>
    public string Subject { get; private set; }

    /// <summary>HTML body with <c>{{Placeholder}}</c>s and optional <c>{{#Key}}…{{/Key}}</c> sections.</summary>
    public string BodyHtml { get; private set; }

    public void Update(string subject, string bodyHtml)
    {
        Subject = Guard.Required(subject, "EMAIL_TEMPLATE_SUBJECT_REQUIRED");
        BodyHtml = Guard.Required(bodyHtml, "EMAIL_TEMPLATE_BODY_REQUIRED");
    }
}
