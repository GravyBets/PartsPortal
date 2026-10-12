# PartsPortal releases

Phase 3 hosts client installers under:

`http://10.130.206.135/partsportal/install/`

For release 3.2.6:

1. Build/publish `PartsPortal.Client` using `FolderProfile.pubxml`.
2. Build the `PartsPortal.Setup` Release configuration in Visual Studio.
3. Copy `PartsPortal-Setup-3.2.6.msi` to `/var/www/partsportal/install/`.
4. Copy/update `index.html` in that directory.
5. Update `ClientVersion__LatestVersion`, `ClientVersion__DownloadUrl`, and release-note environment values in `/etc/partsportal/partsportal-api.env`.
6. Restart `partsportal-api`.

The first deployment of 3.2.6 is a manual upgrade from 3.2.5 because 3.2.5
does not yet contain the update-check client. Releases after 3.2.6 can be
advertised automatically through the API.
