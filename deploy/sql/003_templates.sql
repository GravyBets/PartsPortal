-- PartsPortal central templates
-- Safe to run repeatedly.

USE partsportal;

CREATE TABLE IF NOT EXISTS templates
(
    id INT NOT NULL AUTO_INCREMENT,
    name VARCHAR(100) NOT NULL,
    warehouse_id INT NOT NULL,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by VARCHAR(128) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY ux_templates_warehouse_name (warehouse_id, name),
    KEY ix_templates_warehouse_active_name (warehouse_id, is_active, name),
    CONSTRAINT fk_templates_warehouse
        FOREIGN KEY (warehouse_id)
        REFERENCES warehouses (id)
        ON DELETE RESTRICT
)
ENGINE=InnoDB
DEFAULT CHARSET=utf8mb4
COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS template_lines
(
    id INT NOT NULL AUTO_INCREMENT,
    template_id INT NOT NULL,
    part_id INT NOT NULL,
    sort_order INT NOT NULL DEFAULT 0,
    quantity INT NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY ux_template_lines_template_part (template_id, part_id),
    KEY ix_template_lines_template_sort (template_id, sort_order),
    KEY ix_template_lines_part_id (part_id),
    CONSTRAINT fk_template_lines_template
        FOREIGN KEY (template_id)
        REFERENCES templates (id)
        ON DELETE CASCADE,
    CONSTRAINT fk_template_lines_part
        FOREIGN KEY (part_id)
        REFERENCES parts (id)
        ON DELETE RESTRICT
)
ENGINE=InnoDB
DEFAULT CHARSET=utf8mb4
COLLATE=utf8mb4_unicode_ci;
