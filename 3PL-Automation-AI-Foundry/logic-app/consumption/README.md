# Consumption-tier alternative (recommended — cuts the ~$150+/month Standard hosting cost)

The 4 workflows in the parent `logic-app/` folder target **Logic Apps Standard**, which bills a
flat ~$150–170/month for its dedicated Workflow Standard (WS1) hosting plan whether or not anything
runs. Nothing in these workflows (HTTP calls, `If` conditions, Teams adaptive-card wait) needs a
Standard-only feature, so this folder has the same 4 workflows reshaped as **Logic Apps
Consumption** resources — billed per action executed (first 4,000 actions/month free, fractions of
a cent each after), no idle/hosting cost. At this project's request volume that's expected to drop
the Logic Apps bill from ~$150+/month to low single digits.

## What's different from the Standard versions

1. **One Azure resource per workflow**, not 4 workflows inside one app. Consumption has no
   concept of a multi-workflow host — each of these 4 files is a standalone ARM deployment
   template for one `Microsoft.Logic/workflows` resource.
2. **The orchestrator dispatches via HTTP, not the native `"type": "Workflow"` action.** Standard's
   in-app child-workflow action doesn't exist in Consumption (there is no shared app to call
   within). `Invoke_Solace_Subflow`/`Invoke_MuleSoft_Subflow`/`Invoke_BTP_Subflow` now `POST` to
   each child's own HTTP trigger callback URL instead. This is also a clean-up, not just a
   tier-driven workaround: the parent README previously flagged that the native action's exact
   "started vs. finished" completion semantics needed confirming against the live runtime before
   trusting the fire-and-acknowledge behavior — an explicit HTTP call removes that ambiguity.
3. **Each per-platform workflow now responds 202 immediately off its own trigger**
   (`Response_Acknowledged`, parallel to `Generate_*`, both `runAfter: {}`), instead of the
   Standard version's single `Response_Final_Status` sent at the very end of the run. A Logic
   Apps run can only send one synchronous HTTP response — sending it early (so the orchestrator's
   `Invoke_*_Subflow` call returns in ~1s instead of blocking on a multi-hour Teams approval) means
   there's no second response left to send at the end. The final decision + publish result are
   composed into the run's history (`Compose_Final_Status`) for introspection in the Designer, and
   — as was already true before this change — the authoritative final status lives in the matching
   `{platform}_requests` Cosmos record, updated by the agent's publish phase regardless of which
   Logic App tier calls it.
4. **The `teams` API connection is reused as-is.** `Microsoft.Web/connections` (managed API
   connections) work identically under Standard and Consumption — the existing `teams` connection
   resource and its authentication in `ODL-IBM-2262033` don't need to be recreated, only referenced
   by resource ID (see `teamsConnectionId` parameter in each template).

Trade-off worth knowing: a direct/standalone test call to one of these workflows' trigger URL (not
through the orchestrator) now also gets an immediate 202, not the final approve/reject outcome —
you poll Cosmos for that either way, same as when going through the orchestrator.

## Deploy

Deploy the 3 per-platform workflows first (the orchestrator needs their callback URLs):

```bash
RG=ODL-IBM-2262033

for f in solace-mail-trigger-workflow mulesoft-mail-trigger-workflow btp-mail-trigger-workflow; do
  az deployment group create \
    --resource-group "$RG" \
    --template-file "$f.json" \
    --name "deploy-$f"
done
```

Then fetch each one's HTTP trigger callback URL (this is the Consumption equivalent of a Standard
workflow's trigger URL — it embeds a SAS signature, generated only after deployment):

```bash
for f in solace-mail-trigger-workflow mulesoft-mail-trigger-workflow btp-mail-trigger-workflow; do
  echo "== $f =="
  az rest --method POST \
    --url "https://management.azure.com/subscriptions/a556a28c-c14a-4657-b59f-e0c80f0bb54c/resourceGroups/$RG/providers/Microsoft.Logic/workflows/$f/triggers/manual/listCallbackUrl?api-version=2019-05-01"
done
```

Take the 3 `value` URLs from that output and deploy the orchestrator with them:

```bash
az deployment group create \
  --resource-group "$RG" \
  --template-file 3pl-onboarding-orchestrator-workflow.json \
  --name deploy-orchestrator \
  --parameters \
    solaceWorkflowCallbackUrl="<value from solace-mail-trigger-workflow>" \
    mulesoftWorkflowCallbackUrl="<value from mulesoft-mail-trigger-workflow>" \
    btpWorkflowCallbackUrl="<value from btp-mail-trigger-workflow>"
```

The Teams adaptive card's `groupId`/`channelId` placeholders still need filling in per platform
workflow — same manual step as the Standard version, easiest done by opening each deployed
workflow in the Logic App Designer once and re-picking the Team/Channel on the
`Post_adaptive_card_and_wait_for_a_response` action (the picker writes the real IDs in for you).

## Cutting over the already-live Solace workflow

`solace-mail-trigger-workflow` is the one workflow actually deployed and running today, on the
Standard plan in `ODL-IBM-2262033`. Moving it to Consumption means standing up the new resource
above, re-pointing whatever currently calls the Standard trigger URL (a mail rule, a manual test
script, etc.) at the new Consumption trigger URL, confirming it behaves the same end-to-end for a
test request, and only then deleting the Standard `3pl-automation` Logic App (Standard) resource and
its Workflow Standard hosting plan — that last step is what actually stops the ~$150/month charge,
and is **destructive and irreversible against a live resource**, so do it deliberately and only
once the Consumption replacement is verified working, not as part of this deployment.
