# PartsPortal database

Phase 2 creates the dedicated `partsportal` database boundary but intentionally
does not add catalog tables yet. The parts/catalog schema and EF Core migrations
will be introduced with the centralized parts-management phase.

Run `001_create_database.sql` as a database administrator, then create a
dedicated application login separately and place its connection string in the
VM environment file.
