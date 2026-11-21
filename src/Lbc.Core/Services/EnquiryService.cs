using Lbc.Core.Abstractions;
using Lbc.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lbc.Core.Services;

public sealed record EnquiryRequest(string? Type, string? Name, string? Email, string? Phone, string? Topic, string? EventDate, string? Message);

public sealed class EnquiryService(
    IEnquiryRepository enquiries,
    IEmailSender email,
    IOptions<SiteOptions> site,
    TimeProvider clock,
    ILogger<EnquiryService> logger)
{
    public async Task SubmitAsync(EnquiryRequest r, CancellationToken ct = default)
    {
        if (!Enum.TryParse<EnquiryType>(r.Type, ignoreCase: true, out var type) || !Enum.IsDefined(type))
            type = EnquiryType.Contact;

        DateOnly? eventDate = null;
        if (!string.IsNullOrWhiteSpace(r.EventDate))
        {
            if (!DateOnly.TryParseExact(r.EventDate, "yyyy-MM-dd", out var d)) throw new ValidationException("Please enter a valid date.");
            eventDate = d;
        }

        var enquiry = new Enquiry
        {
            Id = Ids.New(),
            Type = type,
            Name = Guard.Required(r.Name, "Name", 100),
            Email = Guard.Email(r.Email),
            Phone = Guard.Phone(r.Phone, required: false),
            Topic = Guard.Optional(r.Topic, "Topic", 100),
            EventDate = eventDate,
            Message = Guard.Required(r.Message, "Message", 4000),
            CreatedAt = clock.GetUtcNow(),
        };

        await enquiries.AddAsync(enquiry, ct);
        // The enquiry is saved and visible in the admin area, so a mail failure must not make the visitor resubmit.
        try { await email.SendAsync(EmailTemplates.EnquiryNotification(site.Value, enquiry), ct); }
        catch (Exception ex) { logger.LogError(ex, "Failed to email enquiry {EnquiryId}", enquiry.Id); }
    }
}
