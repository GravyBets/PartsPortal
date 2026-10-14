-- PartsPortal central parts catalog
-- Safe to run repeatedly against the partsportal database.

USE partsportal;

CREATE TABLE IF NOT EXISTS warehouses
(
    id INT NOT NULL AUTO_INCREMENT,
    code VARCHAR(32) NOT NULL,
    name VARCHAR(128) NOT NULL,
    subtitle VARCHAR(128) NULL,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    sort_order INT NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    UNIQUE KEY ux_warehouses_code (code)
)
ENGINE=InnoDB
DEFAULT CHARSET=utf8mb4
COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS parts
(
    id INT NOT NULL AUTO_INCREMENT,
    material_number VARCHAR(64) NOT NULL,
    description VARCHAR(255) NOT NULL,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by VARCHAR(128) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY ux_parts_material_number (material_number),
    KEY ix_parts_active_description (is_active, description)
)
ENGINE=InnoDB
DEFAULT CHARSET=utf8mb4
COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS part_warehouses
(
    part_id INT NOT NULL,
    warehouse_id INT NOT NULL,
    PRIMARY KEY (part_id, warehouse_id),
    KEY ix_part_warehouses_warehouse_id (warehouse_id),
    CONSTRAINT fk_part_warehouses_part
        FOREIGN KEY (part_id)
        REFERENCES parts (id)
        ON DELETE CASCADE,
    CONSTRAINT fk_part_warehouses_warehouse
        FOREIGN KEY (warehouse_id)
        REFERENCES warehouses (id)
        ON DELETE RESTRICT
)
ENGINE=InnoDB
DEFAULT CHARSET=utf8mb4
COLLATE=utf8mb4_unicode_ci;

INSERT INTO warehouses (id, code, name, subtitle, is_active, sort_order)
VALUES
    (1, 'BUILDING_D', 'Warehouse - Building D', 'Meter Lab', 1, 10),
    (2, 'BUILDING_A', 'Warehouse - Building A', 'Central', 1, 20),
    (3, 'BUILDING_B', 'Warehouse - Building B', 'Substation', 1, 30)
ON DUPLICATE KEY UPDATE
    name = VALUES(name),
    subtitle = VALUES(subtitle),
    is_active = VALUES(is_active),
    sort_order = VALUES(sort_order);
