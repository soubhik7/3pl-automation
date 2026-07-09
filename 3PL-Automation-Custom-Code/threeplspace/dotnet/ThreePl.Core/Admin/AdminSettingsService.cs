using Microsoft.EntityFrameworkCore;
using ThreePl.Core.Data;
using ThreePl.Core.Entities;
using ThreePl.Core.Forms;

namespace ThreePl.Core.Admin;

/// <summary>
/// Default approval email addresses configured on the Admin tab: the SME
/// (recipient) address per domain and the architecture-approval address.
/// The domain defaults pre-fill each intake form's Recipient Email whenever
/// it is empty; the architecture address is informational (the launcher
/// workflow's approver recipient is configured on the Logic App itself).
/// </summary>
public sealed class DefaultEmails
{
    public string? BtpSmeEmail { get; set; }
    public string? SolaceSmeEmail { get; set; }
    public string? MuleSoftSmeEmail { get; set; }
    public string? ArchitectureApprovalEmail { get; set; }

    /// <summary>Pre-fills each form's Recipient Email when it is empty (saved values always win).</summary>
    public void ApplyTo(BtpForm? btp, SolaceForm? solace, MuleSoftForm? muleSoft)
    {
        if (btp is not null && string.IsNullOrWhiteSpace(btp.RecipientEmail) && !string.IsNullOrWhiteSpace(BtpSmeEmail))
            btp.RecipientEmail = BtpSmeEmail;
        if (solace is not null && string.IsNullOrWhiteSpace(solace.RecipientEmail) && !string.IsNullOrWhiteSpace(SolaceSmeEmail))
            solace.RecipientEmail = SolaceSmeEmail;
        if (muleSoft is not null && string.IsNullOrWhiteSpace(muleSoft.RecipientEmail) && !string.IsNullOrWhiteSpace(MuleSoftSmeEmail))
            muleSoft.RecipientEmail = MuleSoftSmeEmail;
    }
}

/// <summary>
/// Server-persisted portal settings over dbo.AdminSetting (shared across
/// users). Read failures (table not created yet / DB unreachable) degrade to
/// empty defaults instead of breaking the portal.
/// </summary>
public class AdminSettingsService
{
    public const string BtpSmeEmailKey = "BtpSmeEmail";
    public const string SolaceSmeEmailKey = "SolaceSmeEmail";
    public const string MuleSoftSmeEmailKey = "MuleSoftSmeEmail";
    public const string ArchitectureApprovalEmailKey = "ArchitectureApprovalEmail";

    private readonly IDbContextFactory<OnboardingDbContext> _dbFactory;

    public AdminSettingsService(IDbContextFactory<OnboardingDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<DefaultEmails> GetDefaultEmailsAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var rows = await db.AdminSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value, ct);
            return new DefaultEmails
            {
                BtpSmeEmail = rows.GetValueOrDefault(BtpSmeEmailKey),
                SolaceSmeEmail = rows.GetValueOrDefault(SolaceSmeEmailKey),
                MuleSoftSmeEmail = rows.GetValueOrDefault(MuleSoftSmeEmailKey),
                ArchitectureApprovalEmail = rows.GetValueOrDefault(ArchitectureApprovalEmailKey),
            };
        }
        catch (Exception)
        {
            // Table missing / DB unreachable — no defaults, portal keeps working.
            return new DefaultEmails();
        }
    }

    public async Task SaveDefaultEmailsAsync(DefaultEmails emails, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await UpsertAsync(db, BtpSmeEmailKey, emails.BtpSmeEmail, ct);
        await UpsertAsync(db, SolaceSmeEmailKey, emails.SolaceSmeEmail, ct);
        await UpsertAsync(db, MuleSoftSmeEmailKey, emails.MuleSoftSmeEmail, ct);
        await UpsertAsync(db, ArchitectureApprovalEmailKey, emails.ArchitectureApprovalEmail, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task UpsertAsync(OnboardingDbContext db, string key, string? value, CancellationToken ct)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var row = await db.AdminSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row is null)
            db.AdminSettings.Add(new AdminSetting { Key = key, Value = trimmed, UpdatedAt = DateTime.UtcNow });
        else
        {
            row.Value = trimmed;
            row.UpdatedAt = DateTime.UtcNow;
        }
    }
}
