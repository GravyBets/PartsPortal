PARTSPORTAL CLICKONCE PUSH UPDATE
================================

PartsPortal now uses ClickOnce for client installation and updates.

Normal release:
  .\Push-Update.ps1

Or double-click:
  Push-Update.cmd

The script asks for:
  1. New PartsPortal version (defaults to the next patch)
  2. Release notes
  3. One final confirmation

Then it automatically:
  - Updates PartsPortal assembly/file/informational versions
  - Updates the ClickOnce ApplicationVersion
  - Updates API release metadata
  - Rebuilds the internal download page
  - Builds Contracts, API, and Client
  - Publishes a self-contained win-x64 ClickOnce deployment with MSBuild
  - Packages the full ClickOnce deployment folder
  - SCPs it to 10.130.206.135
  - Copies the deployment manifest and versioned application files into nginx
  - Updates ONLY ClientVersion__* values in the VM env file
  - Restarts partsportal-api
  - Verifies the live version endpoint
  - Verifies PartsPortal.application is downloadable
  - Commits and pushes the release to Git

Useful:
  .\Push-Update.ps1 -SkipDeploy
  .\Push-Update.ps1 -SkipGit

Example:
  .\Push-Update.ps1 -Version 3.2.10 -ReleaseNotes @(
      "Converted PartsPortal deployment to ClickOnce.",
      "Future releases update automatically at app startup."
  )

ONE-TIME MSI -> CLICKONCE TRANSITION
-----------------------------------
The existing MSI-installed PartsPortal and the ClickOnce-installed PartsPortal
are separate Windows installations. For the first ClickOnce release only:

  1. Publish the ClickOnce release.
  2. Uninstall the old MSI PartsPortal entry.
  3. Open http://10.130.206.135/partsportal/install/
  4. Install PartsPortal from PartsPortal.application.

PartsPortal user settings are stored under the user's AppData PartsPortal folder,
so this deployment transition does not intentionally remove those settings.

After that first ClickOnce install, later ClickOnce releases are detected at app
startup by ClickOnce itself.
