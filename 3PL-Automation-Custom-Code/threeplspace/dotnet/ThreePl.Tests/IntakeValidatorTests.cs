using ThreePl.Core.Admin;
using ThreePl.Core.Forms;

namespace ThreePl.Tests;

public class IntakeValidatorTests
{
    private static Dictionary<(string, string), RequirementLevel> DefaultLevels()
    {
        var levels = new Dictionary<(string, string), RequirementLevel>();
        foreach (var domain in FieldDefinitions.All)
            foreach (var field in domain.Fields)
                levels[(domain.Domain, field.Name)] = field.DefaultLevel;
        return levels;
    }

    [Fact]
    public void Btp_Outbound_MissingRequiredFields_Blocks()
    {
        var form = new BtpForm { SubAccount = "sa", ProductName = "pn", Environment = "uat" };
        var result = IntakeValidator.Validate("Btp", form.Direction, form.FieldValues,
            new Dictionary<string, int>(), DefaultLevels());

        Assert.False(result.IsValid);
        Assert.Contains("Mode", result.MissingLabels);
        Assert.Contains("Service Exists", result.MissingLabels);
        Assert.Contains("mode", result.InvalidFields);
        // Optional fields never block.
        Assert.DoesNotContain("Short Text", result.MissingLabels);
    }

    [Fact]
    public void Btp_Inbound_OnlyNaturalKeysRequired()
    {
        var form = new BtpForm { Direction = "Inbound", SubAccount = "sa", ProductName = "pn", Environment = "uat" };
        var result = IntakeValidator.Validate("Btp", form.Direction, form.FieldValues,
            new Dictionary<string, int>(), DefaultLevels());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Btp_Inbound_MissingNaturalKey_StillBlocks()
    {
        var form = new BtpForm { Direction = "Inbound", SubAccount = "sa", ProductName = "pn" };
        var result = IntakeValidator.Validate("Btp", form.Direction, form.FieldValues,
            new Dictionary<string, int>(), DefaultLevels());
        Assert.False(result.IsValid);
        Assert.Contains("Environment", result.MissingLabels);
    }

    [Fact]
    public void EmailField_FilledWithoutAtSign_IsInvalid()
    {
        var form = new BtpForm
        {
            Direction = "Inbound", SubAccount = "sa", ProductName = "pn", Environment = "uat",
            RecipientEmail = "not-an-email",
        };
        var result = IntakeValidator.Validate("Btp", form.Direction, form.FieldValues,
            new Dictionary<string, int>(), DefaultLevels());
        Assert.False(result.IsValid);
        Assert.Contains("Recipient Email (invalid email)", result.MissingLabels);
    }

    [Fact]
    public void Solace_Outbound_EmptyMessageTypesTable_Blocks()
    {
        var form = new SolaceForm
        {
            Brand = "b", Env = "e", SystemName = "s", ThreePLCode = "t",
            Action = "a", ServiceExists = "true",
            RepoOwner = "o", RepoName = "r", FilePath = "f", Branch = "m", BaseBranch = "m",
            FeatureBranchName = "fb", RequesterEmail = "a@b.c", RecipientEmail = "d@e.f",
            CommitMessage = "cm",
        };
        var result = IntakeValidator.Validate("Solace", form.Direction, form.FieldValues,
            form.TableCounts, DefaultLevels());
        Assert.False(result.IsValid);
        Assert.Contains("Message Types (min 1 row)", result.MissingLabels);
        Assert.Contains("messageTypes", result.InvalidFields);

        form.MessageTypes.Add(new SolaceMessageTypeRow { MessageType = "DespatchStock" });
        var retry = IntakeValidator.Validate("Solace", form.Direction, form.FieldValues,
            form.TableCounts, DefaultLevels());
        Assert.True(retry.IsValid);
    }

    [Fact]
    public void AdminOverride_ChangesWhatBlocks()
    {
        var levels = DefaultLevels();
        levels[("Btp", "mode")] = RequirementLevel.Optional;
        levels[("Btp", "shortText")] = RequirementLevel.Always;

        var form = new BtpForm
        {
            SubAccount = "sa", ProductName = "pn", Environment = "uat",
            DeveloperId = "d", Title = "t", RepoOwner = "o", RepoName = "r",
            WorkflowFileName = "w", BranchRef = "b", ServiceExists = "true",
        };
        var result = IntakeValidator.Validate("Btp", form.Direction, form.FieldValues,
            new Dictionary<string, int>(), levels);

        Assert.DoesNotContain("Mode", result.MissingLabels);      // downgraded to Optional
        Assert.Contains("Short Text", result.MissingLabels);      // upgraded to Required
    }

    [Fact]
    public void PruneBlankRows_DropsScaffoldRows_KeepsPartialOnes()
    {
        var form = new SolaceForm();
        form.MessageTypes.Add(new SolaceMessageTypeRow());                                // blank scaffold
        form.MessageTypes.Add(new SolaceMessageTypeRow { Topic = "scx/whm" });            // partially filled
        form.PruneBlankRows();
        var row = Assert.Single(form.MessageTypes);
        Assert.Equal("scx/whm", row.Topic);

        var mule = new MuleSoftForm();
        mule.Environments.Add(new MuleEnvironmentRow());
        mule.UomMappings.Add(new MuleUomMappingRow { UomFrom = "EA" });
        mule.PruneBlankRows();
        Assert.Empty(mule.Environments);
        Assert.Single(mule.UomMappings);
    }
}
