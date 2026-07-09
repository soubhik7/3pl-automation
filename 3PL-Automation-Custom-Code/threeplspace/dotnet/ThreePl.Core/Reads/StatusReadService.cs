using Microsoft.EntityFrameworkCore;
using ThreePl.Core.Data;

namespace ThreePl.Core.Reads;

/// <summary>
/// EF replacement for the enrichment-status workflow's light (poll) response:
/// per-domain status cards, architecture approval, readyToLaunch, and the
/// masked audit trail. EnrichmentStatus is read straight from the stored
/// column (data-enrichment computed it on write); only missingFields is
/// recomputed here, for display.
/// </summary>
public class StatusReadService
{
    private readonly IDbContextFactory<OnboardingDbContext> _dbFactory;

    public StatusReadService(IDbContextFactory<OnboardingDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<OnboardingStatusDto> GetStatusAsync(string correlationId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var btp = await db.BtpConfigs.AsNoTracking()
            .Where(x => x.CorrelationId == correlationId)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        var solace = await db.SolaceClients.AsNoTracking()
            .Where(x => x.CorrelationId == correlationId)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(ct);
        var solaceMtCount = solace is null
            ? 0
            : await db.SolaceMessageTypes.AsNoTracking().CountAsync(x => x.ClientId == solace.Id, ct);

        var mule = await db.MuleSoftPartners.AsNoTracking()
            .Where(x => x.CorrelationId == correlationId)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(ct);
        int muleEnvCount = 0, muleTtCount = 0, muleMtCount = 0, muleSdCount = 0, muleUomCount = 0;
        if (mule is not null)
        {
            muleEnvCount = await db.MuleSoftEnvironments.AsNoTracking().CountAsync(x => x.PartnerId == mule.Id, ct);
            muleTtCount = await db.MuleSoftTransactionTypes.AsNoTracking().CountAsync(x => x.PartnerId == mule.Id, ct);
            muleMtCount = await db.MuleSoftMessageTypes.AsNoTracking().CountAsync(x => x.PartnerId == mule.Id, ct);
            muleSdCount = await db.MuleSoftSourceDestinations.AsNoTracking().CountAsync(x => x.PartnerId == mule.Id, ct);
            muleUomCount = await db.MuleSoftUomMappings.AsNoTracking().CountAsync(x => x.PartnerId == mule.Id, ct);
        }

        var approval = await db.OnboardingApprovals.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId, ct);

        var audit = await db.EnrichmentAuditLogs.AsNoTracking()
            .Where(x => x.CorrelationId == correlationId)
            .OrderBy(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        var btpDto = btp is null
            ? DomainStatusDto.NotFound()
            : new DomainStatusDto
            {
                Found = true,
                EnrichmentStatus = btp.EnrichmentStatus,
                DeploymentStatus = btp.DeploymentStatus,
                CardSentAt = btp.CardSentAt,
                CardRespondedAt = btp.CardRespondedAt,
                Direction = btp.Direction,
                MissingFields = MissingFieldRules.ForBtp(btp),
            };

        var solaceDto = solace is null
            ? DomainStatusDto.NotFound()
            : new DomainStatusDto
            {
                Found = true,
                EnrichmentStatus = solace.EnrichmentStatus,
                DeploymentStatus = solace.DeploymentStatus,
                CardSentAt = solace.CardSentAt,
                CardRespondedAt = solace.CardRespondedAt,
                Direction = solace.Direction,
                BranchApprovalStatus = solace.BranchApprovalStatus,
                PendingBranchName = solace.PendingBranchName,
                MissingFields = MissingFieldRules.ForSolace(solace, solaceMtCount),
            };

        var muleDto = mule is null
            ? DomainStatusDto.NotFound()
            : new DomainStatusDto
            {
                Found = true,
                EnrichmentStatus = mule.EnrichmentStatus,
                DeploymentStatus = mule.DeploymentStatus,
                CardSentAt = mule.CardSentAt,
                CardRespondedAt = mule.CardRespondedAt,
                Direction = mule.Direction,
                BranchApprovalStatus = mule.BranchApprovalStatus,
                PendingBranchName = mule.PendingBranchName,
                MissingFields = MissingFieldRules.ForMuleSoft(
                    mule, muleEnvCount, muleTtCount, muleMtCount, muleSdCount, muleUomCount),
            };

        return new OnboardingStatusDto
        {
            CorrelationId = correlationId,
            // Same expression as the enrichment-status Response-200: all three
            // domain rows exist and every EnrichmentStatus is Complete.
            ReadyToLaunch = btpDto.Found && solaceDto.Found && muleDto.Found
                && btpDto.EnrichmentStatus == "Complete"
                && solaceDto.EnrichmentStatus == "Complete"
                && muleDto.EnrichmentStatus == "Complete",
            ArchitectureApproval = new ArchitectureApprovalDto
            {
                Status = approval?.ArchitectureApprovalStatus ?? "NotRequested",
                ApproverEmail = approval?.ApproverEmail,
                RespondedAt = approval?.RespondedAt,
            },
            Btp = btpDto,
            Solace = solaceDto,
            MuleSoft = muleDto,
            AuditTrail = audit.Select(a => new AuditEntryDto
            {
                Id = a.Id,
                Domain = a.Domain,
                CorrelationId = a.CorrelationId,
                EntityKey = a.EntityKey,
                Channel = a.Channel,
                ActorEmail = EmailMasker.Mask(a.ActorEmail),
                EventType = a.EventType,
                EventDetail = a.EventDetail,
                CreatedAt = a.CreatedAt,
            }).ToList(),
        };
    }
}
