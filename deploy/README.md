# PartsPortal API deployment

PartsPortal is hosted independently from SmartGridSuite on the existing Linux VM.

## VM layout

- `/opt/partsportal/api` - self-contained Linux API publish
- `/etc/partsportal/partsportal-api.env` - server-only environment/secrets
- `/var/log/partsportal/api.log` - API log
- `/etc/init.d/partsportal-api` - SysV init service
- API listener: `127.0.0.1:5085`

The API should be published as a self-contained `linux-x64` application so the
VM does not require a machine-wide .NET runtime.

The real database password belongs only in
`/etc/partsportal/partsportal-api.env`. Never store it in source control.

## Initial verification endpoints

Direct on the VM:

- `http://127.0.0.1:5085/`
- `http://127.0.0.1:5085/api/health`
- `http://127.0.0.1:5085/api/system/client-version`

## nginx plan

SmartGridSuite already owns the VM's default server and `/api/` path.
PartsPortal will therefore use its own path prefix instead of changing the
existing SmartGridSuite route.

Planned public paths:

- `/partsportal/api/...` -> `http://127.0.0.1:5085/api/...`
- `/partsportal/install/` -> PartsPortal installer/download files

The install/download site is finalized in Phase 3.
