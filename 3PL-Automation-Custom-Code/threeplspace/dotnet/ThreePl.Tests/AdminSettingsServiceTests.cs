using ThreePl.Core.Admin;
using ThreePl.Core.Forms;

namespace ThreePl.Tests;

public class AdminSettingsServiceTests : IDisposable
{
    private readonly TestDbFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Defaults_AreEmpty_WhenNothingConfigured()
    {
        var service = new AdminSettingsService(_factory);
        var emails = await service.GetDefaultEmailsAsync();
        Assert.Null(emails.BtpSmeEmail);
        Assert.Null(emails.SolaceSmeEmail);
        Assert.Null(emails.MuleSoftSmeEmail);
        Assert.Null(emails.ArchitectureApprovalEmail);
    }

    [Fact]
    public async Task SaveAndReload_RoundTrips_AcrossServiceInstances()
    {
        var service = new AdminSettingsService(_factory);
        await service.SaveDefaultEmailsAsync(new DefaultEmails
        {
            BtpSmeEmail = "btp-sme@example.com",
            SolaceSmeEmail = "solace-sme@example.com",
            MuleSoftSmeEmail = "mule-sme@example.com",
            ArchitectureApprovalEmail = "arch-board@example.com",
        });

        var other = new AdminSettingsService(_factory);
        var emails = await other.GetDefaultEmailsAsync();
        Assert.Equal("btp-sme@example.com", emails.BtpSmeEmail);
        Assert.Equal("solace-sme@example.com", emails.SolaceSmeEmail);
        Assert.Equal("mule-sme@example.com", emails.MuleSoftSmeEmail);
        Assert.Equal("arch-board@example.com", emails.ArchitectureApprovalEmail);
    }

    [Fact]
    public async Task Save_Twice_Upserts_OneRowPerKey_AndClearsWithBlank()
    {
        var service = new AdminSettingsService(_factory);
        await service.SaveDefaultEmailsAsync(new DefaultEmails { BtpSmeEmail = "old@example.com" });
        await service.SaveDefaultEmailsAsync(new DefaultEmails { BtpSmeEmail = "  new@example.com  ", SolaceSmeEmail = "" });

        var emails = await service.GetDefaultEmailsAsync();
        Assert.Equal("new@example.com", emails.BtpSmeEmail); // trimmed + overwritten
        Assert.Null(emails.SolaceSmeEmail);                  // blank clears

        using var db = _factory.CreateDbContext();
        Assert.Equal(1, db.AdminSettings.Count(x => x.Key == AdminSettingsService.BtpSmeEmailKey));
    }

    [Fact]
    public void ApplyTo_FillsOnlyEmptyRecipientEmails_SavedValuesWin()
    {
        var defaults = new DefaultEmails
        {
            BtpSmeEmail = "btp-sme@example.com",
            SolaceSmeEmail = "solace-sme@example.com",
            MuleSoftSmeEmail = "mule-sme@example.com",
        };
        var btp = new BtpForm();                                            // empty → filled
        var solace = new SolaceForm { RecipientEmail = "saved@example.com" }; // saved → untouched
        MuleSoftForm? mule = null;                                          // absent form tolerated

        defaults.ApplyTo(btp, solace, mule);

        Assert.Equal("btp-sme@example.com", btp.RecipientEmail);
        Assert.Equal("saved@example.com", solace.RecipientEmail);
    }

    [Fact]
    public void ApplyTo_LeavesFormsAlone_WhenNoDefaultsConfigured()
    {
        var btp = new BtpForm();
        new DefaultEmails().ApplyTo(btp, new SolaceForm(), new MuleSoftForm());
        Assert.Null(btp.RecipientEmail);
    }
}
