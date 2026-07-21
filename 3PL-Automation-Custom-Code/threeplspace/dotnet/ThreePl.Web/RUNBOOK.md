# ThreePl.Web — Run & Deploy

## Prerequisites

- .NET 8 SDK
- **ASP.NET Core 8.x runtime** (`Microsoft.AspNetCore.App`, not just the base `Microsoft.NETCore.App`). Check with:
  ```powershell
  dotnet --list-runtimes
  ```
  If `Microsoft.AspNetCore.App 8.x` is missing, install it:
  ```powershell
  winget install --id Microsoft.DotNet.AspNetCore.8
  ```
  Without it, `dotnet run` fails with "You must install or update .NET to run this application," and running the net8 build roll-forwarded onto a different major version serves `_framework/blazor.web.js` as a 404 — pages prerender but never become interactive.

## Run locally

```powershell
cd C:\Users\SoubhikMukhopadhyay\source\repos\automation\3PL-Automation-Custom-Code\threeplspace\dotnet\ThreePl.Web
dotnet run
```

Opens on `http://localhost:5199` (see `Properties/launchSettings.json`). Use `dotnet watch run` instead for hot reload during UI work.

### Database

`appsettings.json` points at live Azure SQL (`ConnectionStrings:OnboardingDb`) with `Database:Provider=SqlServer`. That only works if this machine's IP is allowed through the SQL server firewall.

If it isn't, add a `Database` block to the gitignored `appsettings.Local.json` to fall back to a local SQLite file instead (`Program.cs` already branches on `Database:Provider`):

```json
{
  "Database": { "Provider": "Sqlite", "EnsureCreated": true }
}
```

The write path (Logic App calls) always goes to the real, live Logic App regardless of the read provider — the SAS URLs for that live in `appsettings.Local.json`'s `LogicApps` block, which isn't optional either way.

### Tests

```powershell
cd C:\Users\SoubhikMukhopadhyay\source\repos\automation\3PL-Automation-Custom-Code\threeplspace\dotnet
dotnet test
```

## Deploy

There is currently no deployment pipeline for this project (no GitHub Actions workflow, no Bicep/ARM template, no App Service resource identified yet). `docs/blazor-frontend-rewrite-plan.md` lists deploying this app to App Service as a still-open follow-up. Until that's set up, deploy manually:

```powershell
# 1. Publish a Release build
cd C:\Users\SoubhikMukhopadhyay\source\repos\automation\3PL-Automation-Custom-Code\threeplspace\dotnet\ThreePl.Web
dotnet publish -c Release -o .\publish

# 2. First time only — create the App Service (skip if one already exists)
az webapp up --name <app-name> --resource-group <resource-group> --runtime "DOTNETCORE:8.0" --sku B1 --location <region>

# 3. Subsequent deploys — zip and push to an existing App Service
Compress-Archive -Path .\publish\* -DestinationPath .\publish.zip -Force
az webapp deploy --name <app-name> --resource-group <resource-group> --src-path .\publish.zip --type zip
```

### Before this goes to production

- Move the SQL connection string and `AdminAuth` credentials out of plaintext config. They currently live only in the gitignored local `appsettings.json`/`appsettings.Local.json` (never committed), which is fine for local dev, but they must not be baked into the publish output or copied verbatim into App Service app settings. Use Managed Identity for the SQL connection and Key Vault (or App Service app settings, which are encrypted at rest) for anything else.
- Set `ASPNETCORE_ENVIRONMENT=Production` and the real connection string as App Service configuration, not in a checked-in or published `appsettings.json`.
- Confirm the SQL firewall allows the App Service's outbound IP (or use a VNet/Managed Identity path instead of IP allowlisting).
