using Microsoft.EntityFrameworkCore;
using ThreePl.Core.Data;
using ThreePl.Core.Forms;

namespace ThreePl.Core.Reads;

public sealed class IntakePrefillDto
{
    public CommonForm? Common { get; init; }
    public BtpForm? Btp { get; init; }
    public SolaceForm? Solace { get; init; }
    public MuleSoftForm? MuleSoft { get; init; }
}

/// <summary>
/// Full saved records + child arrays for prefilling the intake forms when a
/// past session is opened (EF replacement for enrichment-status's
/// includeDetail:true response). EncryptedPassword is never prefilled — a
/// blank password field means "leave the stored value unchanged".
/// </summary>
public class IntakePrefillService
{
    private readonly IDbContextFactory<OnboardingDbContext> _dbFactory;

    public IntakePrefillService(IDbContextFactory<OnboardingDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<IntakePrefillDto> GetPrefillAsync(string correlationId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var onboarding = await db.Onboardings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId, ct);

        var btp = await db.BtpConfigs.AsNoTracking()
            .Where(x => x.CorrelationId == correlationId)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        var solace = await db.SolaceClients.AsNoTracking()
            .Include(x => x.MessageTypes)
            .Where(x => x.CorrelationId == correlationId)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        var mule = await db.MuleSoftPartners.AsNoTracking()
            .Include(x => x.Environments)
            .Include(x => x.TransactionTypes)
            .Include(x => x.MessageTypes)
            .Include(x => x.SourceDestinations)
            .Include(x => x.UomMappings)
            .Where(x => x.CorrelationId == correlationId)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        return new IntakePrefillDto
        {
            Common = onboarding is null || onboarding.UpdatedAt is null && onboarding.InterfaceId is null
                ? null
                : new CommonForm
                {
                    InterfaceId = onboarding.InterfaceId,
                    EaRef = onboarding.EaRef,
                    SourceApp = onboarding.SourceApp,
                    TargetApp = onboarding.TargetApp,
                    BusinessObject = onboarding.BusinessObject,
                    Country = onboarding.Country,
                    SourceFormat = onboarding.SourceFormat,
                    SourceInterfaceType = onboarding.SourceInterfaceType,
                    TargetFormat = onboarding.TargetFormat,
                    TargetInterfaceType = onboarding.TargetInterfaceType,
                    FunctionalDescription = onboarding.FunctionalDescription,
                    Volume = onboarding.Volume,
                    SizePerMessage = onboarding.SizePerMessage,
                    PeakVolume = onboarding.PeakVolume,
                    ThreePlPartnerId = onboarding.ThreePlPartnerId,
                    NavInstanceId = onboarding.NavInstanceId,
                    CountryCodeIso = onboarding.CountryCodeIso,
                    RegionIso = onboarding.RegionIso,
                    SubscriptionRules = onboarding.SubscriptionRules,
                },
            Btp = btp is null
                ? null
                : new BtpForm
                {
                    Direction = btp.Direction,
                    SubAccount = btp.SubAccount,
                    ProductName = btp.ProductName,
                    Environment = btp.Environment,
                    Mode = btp.Mode,
                    DeveloperId = btp.DeveloperId,
                    Title = btp.Title,
                    ShortText = btp.ShortText,
                    RepoOwner = btp.RepoOwner,
                    RepoName = btp.RepoName,
                    WorkflowFileName = btp.WorkflowFileName,
                    BranchRef = btp.BranchRef,
                    ServiceExists = TriStateString(btp.ServiceExists),
                },
            Solace = solace is null
                ? null
                : new SolaceForm
                {
                    Direction = solace.Direction,
                    Brand = solace.Brand,
                    Env = solace.Env,
                    SystemName = solace.SystemName,
                    ThreePLCode = solace.ThreePLCode,
                    EncryptedPassword = null, // privacy: never prefill
                    Action = solace.Action,
                    AclClientConnectDefaultAction = solace.AclClientConnectDefaultAction ?? "allow",
                    AclPublishTopicDefaultAction = solace.AclPublishTopicDefaultAction ?? "allow",
                    AclSubscribeShareNameDefaultAction = solace.AclSubscribeShareNameDefaultAction ?? "allow",
                    AclSubscribeTopicDefaultAction = solace.AclSubscribeTopicDefaultAction ?? "allow",
                    ClientProfileAllowGuaranteedMsgSendEnabled = solace.ClientProfileAllowGuaranteedMsgSendEnabled ?? false,
                    ClientProfileAllowGuaranteedMsgReceiveEnabled = solace.ClientProfileAllowGuaranteedMsgReceiveEnabled ?? false,
                    ClientProfileCompressionEnabled = solace.ClientProfileCompressionEnabled ?? false,
                    ClientProfileReplicationAllowClientConnectWhenStandbyEnabled = solace.ClientProfileReplicationAllowClientConnectWhenStandbyEnabled ?? false,
                    ClientProfileAllowTransactedSessionsEnabled = solace.ClientProfileAllowTransactedSessionsEnabled ?? false,
                    ClientProfileAllowBridgeConnectionsEnabled = solace.ClientProfileAllowBridgeConnectionsEnabled ?? false,
                    ClientProfileAllowGuaranteedEndpointCreateEnabled = solace.ClientProfileAllowGuaranteedEndpointCreateEnabled ?? false,
                    ClientProfileAllowSharedSubscriptionsEnabled = solace.ClientProfileAllowSharedSubscriptionsEnabled ?? false,
                    ClientUserEnabled = solace.ClientUserEnabled ?? false,
                    ClientUserGuaranteedEndpointPermissionOverrideEnabled = solace.ClientUserGuaranteedEndpointPermissionOverrideEnabled ?? false,
                    ClientUserSubscriptionManagerEnabled = solace.ClientUserSubscriptionManagerEnabled ?? false,
                    ServiceExists = TriStateString(solace.ServiceExists),
                    RepoOwner = solace.RepoOwner,
                    RepoName = solace.RepoName,
                    FilePath = solace.FilePath,
                    Branch = solace.Branch,
                    BaseBranch = solace.BaseBranch,
                    FeatureBranchName = solace.FeatureBranchName,
                    RequesterEmail = solace.RequesterEmail,
                    RecipientEmail = solace.RecipientEmail,
                    CommitMessage = solace.CommitMessage,
                    MessageTypes = solace.MessageTypes.OrderBy(m => m.Id).Select(m => new SolaceMessageTypeRow
                    {
                        MessageType = m.MessageType,
                        Topic = m.Topic,
                        QueuePermission = m.QueuePermission,
                        QueueEgressEnabled = m.QueueEgressEnabled ?? false,
                        QueueMaxRedeliveryCount = m.QueueMaxRedeliveryCount,
                    }).ToList(),
                },
            MuleSoft = mule is null
                ? null
                : new MuleSoftForm
                {
                    Direction = mule.Direction,
                    CountryKey = mule.CountryKey,
                    CountryCode = mule.CountryCode,
                    PartnerComment = mule.PartnerComment,
                    CreatedBy = mule.CreatedBy,
                    NavProtocol = mule.NavProtocol,
                    NavPort = mule.NavPort,
                    NavUsername = mule.NavUsername,
                    NavDomain = mule.NavDomain,
                    NavService = mule.NavService,
                    NavSoapPort = mule.NavSoapPort,
                    NavUseCommonCert = mule.NavUseCommonCert ?? false,
                    TranslationReceiverName = mule.TranslationReceiverName,
                    ServiceExists = TriStateString(mule.ServiceExists),
                    RepoOwner = mule.RepoOwner,
                    RepoName = mule.RepoName,
                    FilePathPrefix = mule.FilePathPrefix,
                    Branch = mule.Branch,
                    BaseBranch = mule.BaseBranch,
                    FeatureBranchName = mule.FeatureBranchName,
                    RequesterEmail = mule.RequesterEmail,
                    RecipientEmail = mule.RecipientEmail,
                    CommitMessage = mule.CommitMessage,
                    Environments = mule.Environments.OrderBy(x => x.Id).Select(x => new MuleEnvironmentRow
                    {
                        Environment = x.Environment,
                        NavHost = x.NavHost,
                        NavCompany = x.NavCompany,
                        NavSoapPath = x.NavSoapPath,
                        NavRoutingCode = x.NavRoutingCode,
                    }).ToList(),
                    TransactionTypes = mule.TransactionTypes.OrderBy(x => x.Id).Select(x => new MuleTransactionTypeRow
                    {
                        TransactionTypeCode = x.TransactionTypeCode,
                        TransactionTypeEnabled = x.TransactionTypeEnabled ?? false,
                        TransactionTypeLabel = x.TransactionTypeLabel,
                    }).ToList(),
                    MessageTypes = mule.MessageTypes.OrderBy(x => x.Id).Select(x => new MuleMessageTypeRow
                    {
                        MessageType = x.MessageType,
                    }).ToList(),
                    SourceDestinations = mule.SourceDestinations.OrderBy(x => x.Id).Select(x => new MuleSourceDestinationRow
                    {
                        SourceDestinationFrom = x.SourceDestinationFrom,
                        SourceDestinationTo = x.SourceDestinationTo,
                    }).ToList(),
                    UomMappings = mule.UomMappings.OrderBy(x => x.Id).Select(x => new MuleUomMappingRow
                    {
                        UomFrom = x.UomFrom,
                        UomTo = x.UomTo,
                    }).ToList(),
                },
        };
    }

    private static string TriStateString(bool? value) => value switch
    {
        true => "true",
        false => "false",
        null => "",
    };
}
