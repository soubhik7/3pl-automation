using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ThreePl.Core.Forms;
using ThreePl.Core.Writes;

namespace ThreePl.Tests;

/// <summary>
/// Guards the write contract: the JSON LogicAppClient posts must match what
/// the current HTML posts (same keys, same ''→null / select→bool semantics,
/// same child arrays), and 202/409/500 handling mirrors the old JS.
/// </summary>
public class LogicAppClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string? LastBody;
        public HttpStatusCode StatusCode = HttpStatusCode.OK;
        public string ResponseBody = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (LogicAppClient Client, StubHandler Handler) CreateClient()
    {
        var handler = new StubHandler();
        var options = Options.Create(new LogicAppOptions
        {
            DataEnrichmentUrl = "https://logicapp.example/api/data-enrichment/invoke?sig=x",
            OnboardingLauncherUrl = "https://logicapp.example/api/onboarding-launcher/invoke?sig=y",
            LaunchedBy = "3pl-portal-ui@example.com",
        });
        return (new LogicAppClient(new HttpClient(handler), options), handler);
    }

    [Fact]
    public async Task SaveBtp_PostsSamePayloadShapeAsTheHtml()
    {
        var (client, handler) = CreateClient();
        handler.ResponseBody = """{"enrichmentStatus":"Complete"}""";

        var form = new BtpForm
        {
            Direction = "Outbound",
            SubAccount = "sa", ProductName = "pn", Environment = "uat",
            Mode = "create", DeveloperId = "dev", Title = "t",
            ShortText = "",                    // '' → null, like getFormData
            RepoOwner = "o", RepoName = "r", WorkflowFileName = "w.yml", BranchRef = "main",
            ServiceExists = "true",            // select "true" → boolean true
            RecipientEmail = null,
        };
        var result = await client.SaveDomainAsync("Btp", "CID-1", form.ToPayloadFields());

        Assert.True(result.Success);
        Assert.Equal("Complete", result.EnrichmentStatus);
        Assert.Equal("https://logicapp.example/api/data-enrichment/invoke?sig=x",
            handler.LastRequest!.RequestUri!.ToString());

        var payload = JsonNode.Parse(handler.LastBody!)!.AsObject();
        Assert.Equal("Btp", (string?)payload["domain"]);
        Assert.Equal("CID-1", (string?)payload["correlationId"]);
        Assert.Equal("Outbound", (string?)payload["direction"]);
        Assert.Equal("sa", (string?)payload["subAccount"]);
        Assert.True(payload.ContainsKey("shortText"));
        Assert.Null(payload["shortText"]);                    // '' became null
        Assert.True(payload.ContainsKey("recipientEmail"));
        Assert.Null(payload["recipientEmail"]);
        Assert.True((bool?)payload["serviceExists"]);         // bool, not "true"
        // Exactly the HTML's field set — nothing extra leaks in.
        Assert.Equal(
            new[]
            {
                "domain", "correlationId", "direction", "subAccount", "productName", "environment",
                "mode", "developerId", "title", "shortText", "repoOwner", "repoName",
                "workflowFileName", "branchRef", "serviceExists", "recipientEmail",
            }.OrderBy(x => x),
            payload.Select(kv => kv.Key).OrderBy(x => x));
    }

    [Fact]
    public async Task SaveSolace_IncludesCheckboxBooleans_AndMessageTypesArray()
    {
        var (client, handler) = CreateClient();
        handler.ResponseBody = """{"enrichmentStatus":"AwaitingInput"}""";

        var form = new SolaceForm
        {
            Brand = "petc", Env = "rc", SystemName = "nav", ThreePLCode = "3pl",
            ClientUserEnabled = true,
            ServiceExists = "",               // "Select..." → null
            MessageTypes =
            {
                new SolaceMessageTypeRow
                {
                    MessageType = "DespatchStock", Topic = "scx/whm",
                    QueuePermission = "consume", QueueEgressEnabled = true, QueueMaxRedeliveryCount = 3,
                },
            },
        };
        var result = await client.SaveDomainAsync("Solace", "CID-2", form.ToPayloadFields());

        Assert.True(result.Success);
        Assert.Equal("AwaitingInput", result.EnrichmentStatus);

        var payload = JsonNode.Parse(handler.LastBody!)!.AsObject();
        // Checkboxes are always present booleans (unchecked → false), like the HTML.
        Assert.True((bool?)payload["clientUserEnabled"]);
        Assert.False((bool?)payload["clientProfileCompressionEnabled"]);
        Assert.Null(payload["serviceExists"]);
        Assert.Equal("allow", (string?)payload["aclClientConnectDefaultAction"]);

        var mts = payload["messageTypes"]!.AsArray();
        var mt = Assert.Single(mts)!.AsObject();
        Assert.Equal("DespatchStock", (string?)mt["messageType"]);
        Assert.Equal("scx/whm", (string?)mt["topic"]);
        Assert.Equal("consume", (string?)mt["queuePermission"]);
        Assert.True((bool?)mt["queueEgressEnabled"]);
        Assert.Equal(3, (int?)mt["queueMaxRedeliveryCount"]);
    }

    [Fact]
    public async Task SaveMuleSoft_IncludesAllFiveChildArrays()
    {
        var (client, handler) = CreateClient();
        handler.ResponseBody = """{"enrichmentStatus":"AwaitingInput"}""";

        var form = new MuleSoftForm
        {
            CountryKey = "rc-fr",
            Environments = { new MuleEnvironmentRow { Environment = "dev", NavHost = "h" } },
            TransactionTypes = { new MuleTransactionTypeRow { TransactionTypeCode = "sal_008", TransactionTypeEnabled = true, TransactionTypeLabel = "SHIPMENT" } },
            MessageTypes = { new MuleMessageTypeRow { MessageType = "despatchStock" } },
            SourceDestinations = { new MuleSourceDestinationRow { SourceDestinationFrom = "PLANT-FR1", SourceDestinationTo = "spl_001" } },
            UomMappings = { new MuleUomMappingRow { UomFrom = "EA", UomTo = "UNIT" } },
        };
        await client.SaveDomainAsync("MuleSoft", "CID-3", form.ToPayloadFields());

        var payload = JsonNode.Parse(handler.LastBody!)!.AsObject();
        Assert.Equal("MuleSoft", (string?)payload["domain"]);
        Assert.Single(payload["environments"]!.AsArray());
        Assert.Single(payload["transactionTypes"]!.AsArray());
        Assert.Single(payload["messageTypes"]!.AsArray());
        Assert.Single(payload["sourceDestinations"]!.AsArray());
        Assert.Single(payload["uomMappings"]!.AsArray());
        Assert.Equal("sal_008", (string?)payload["transactionTypes"]![0]!["transactionTypeCode"]);
    }

    [Fact]
    public async Task Save_SurfacesTheWorkflowsCleanErrorBody()
    {
        var (client, handler) = CreateClient();
        handler.StatusCode = HttpStatusCode.InternalServerError;
        handler.ResponseBody = """{"error":"Upsert failed","correlationId":"CID-4"}""";

        var result = await client.SaveDomainAsync("Btp", "CID-4", new BtpForm().ToPayloadFields());

        Assert.False(result.Success);
        Assert.Equal(500, result.StatusCode);
        Assert.Contains("Upsert failed", result.Body);
        Assert.Contains("HTTP 500", result.Error);
    }

    [Fact]
    public async Task Launch_PostsSamePayloadAsTheHtml_AndHandles202()
    {
        var (client, handler) = CreateClient();
        handler.StatusCode = HttpStatusCode.Accepted;
        handler.ResponseBody = """{"correlationId":"CID-5","status":"OrchestrationStarted","domains":["btp","solace","mulesoft"]}""";

        var result = await client.LaunchAsync("CID-5", new[] { "btp", "solace", "mulesoft" }, forceRedeploy: false);

        Assert.True(result.Accepted);
        Assert.Equal(202, result.StatusCode);
        Assert.Equal("OrchestrationStarted", result.Status);
        Assert.Null(result.Error);

        var payload = JsonNode.Parse(handler.LastBody!)!.AsObject();
        Assert.Equal("CID-5", (string?)payload["correlationId"]);
        Assert.Equal(new[] { "btp", "solace", "mulesoft" },
            payload["domains"]!.AsArray().Select(n => (string?)n).ToArray());
        Assert.Equal("3pl-portal-ui@example.com", (string?)payload["launchedBy"]);
        Assert.False((bool?)payload["forceRedeploy"]);
        Assert.Equal(4, payload.Count);
    }

    [Fact]
    public async Task Launch_409GateFailure_SurfacesPerDomainReason()
    {
        var (client, handler) = CreateClient();
        handler.StatusCode = HttpStatusCode.Conflict;
        handler.ResponseBody = """{"error":"Cannot launch onboarding orchestration for the requested domain(s).","correlationId":"CID-6","domains":["btp"]}""";

        var result = await client.LaunchAsync("CID-6", new[] { "btp" }, forceRedeploy: true);

        Assert.False(result.Accepted);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("Cannot launch", result.Error);

        var payload = JsonNode.Parse(handler.LastBody!)!.AsObject();
        Assert.True((bool?)payload["forceRedeploy"]);
    }

    [Fact]
    public async Task Launch_202AwaitingArchitectureApproval_IsAccepted()
    {
        var (client, handler) = CreateClient();
        handler.StatusCode = HttpStatusCode.Accepted;
        handler.ResponseBody = """{"status":"AwaitingArchitectureApproval","correlationId":"CID-7","domains":["btp"],"note":"Architecture approval already requested; not sending a duplicate email."}""";

        var result = await client.LaunchAsync("CID-7", new[] { "btp" }, forceRedeploy: false);

        Assert.True(result.Accepted);
        Assert.Equal("AwaitingArchitectureApproval", result.Status);
        Assert.Contains("duplicate", result.Note);
    }

    [Fact]
    public async Task MissingConfiguration_FailsCleanly_WithoutNetworkCall()
    {
        var handler = new StubHandler();
        var client = new LogicAppClient(new HttpClient(handler), Options.Create(new LogicAppOptions()));

        var save = await client.SaveDomainAsync("Btp", "CID", new JsonObject());
        var launch = await client.LaunchAsync("CID", new[] { "btp" }, false);

        Assert.False(save.Success);
        Assert.Contains("not configured", save.Error);
        Assert.False(launch.Accepted);
        Assert.Contains("not configured", launch.Error);
        Assert.Null(handler.LastRequest);
    }
}
