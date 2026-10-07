# PartsPortal API deployment

The API is intended to run independently from SmartGridSuite on the existing Linux VM.

Recommended layout:

- /opt/partsportal/api - published API files
- /etc/partsportal/partsportal.env - server-only configuration/secrets
- partsportal-api.service - independent systemd service
- HTTP listener: 127.0.0.1:5085

The production database connection string should be supplied through
ConnectionStrings__PartsPortalDb in the server environment file. Do not store
the real database password in appsettings.json or source control.

Initial verification endpoints:

- GET / -> API process status
- GET /api/health -> API and database reachability
- GET /api/system/client-version -> client version policy

The reverse-proxy/download URL will be finalized when Phase 3 adds the
PartsPortal release/update site.
