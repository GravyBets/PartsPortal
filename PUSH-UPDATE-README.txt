PARTSPORTAL PUSH UPDATE
=======================

Put Push-Update.ps1 and Push-Update.cmd in the ROOT of the PartsPortal repo,
next to PartsPortal.sln.

Easiest use:
  Double-click Push-Update.cmd

Or from PowerShell:
  .\Push-Update.ps1

It will ask for:
  1. New PartsPortal version (defaults to the next patch)
  2. Minimum supported version
  3. Release notes (one per line)
  4. One final confirmation

Then it automatically:
  - Updates PartsPortal.Client version metadata
  - Updates the MSI ProductCode / PackageCode and installer version
  - Keeps the existing MSI UpgradeCode
  - Updates API release metadata
  - Rebuilds the download page
  - Builds Contracts, API, and Client
  - Publishes the self-contained Windows client
  - Builds PartsPortal.Setup in Release
  - Creates ONE release ZIP
  - SCPs it to 10.130.206.135
  - Installs the MSI/download page on nginx
  - Updates ONLY ClientVersion__* values in the VM env file
  - Does NOT replace or expose the PartsPortal DB password
  - Restarts partsportal-api
  - Verifies the live API version
  - Verifies the MSI URL
  - Commits and pushes the release to Git

The VM helper keeps backups of the prior environment/index/MSI and rolls the
server policy back if deployment verification fails.

Useful:
  .\Push-Update.ps1 -SkipDeploy
  .\Push-Update.ps1 -SkipGit

Example:
  .\Push-Update.ps1 -Version 3.2.7 -ReleaseNotes @(
      "Fixed automatic update notifications.",
      "Finished PartsPortal branding."
  )

The script intentionally refuses to start when Git has uncommitted changes so
unrelated work cannot accidentally get swept into a release commit.
