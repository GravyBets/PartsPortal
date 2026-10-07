CREATE DATABASE IF NOT EXISTS partsportal
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

-- Create the application login separately on the VM/DB host so credentials
-- never live in source control. Example shape only:
--
-- CREATE USER 'partsportal_app'@'127.0.0.1' IDENTIFIED BY '<strong-password>';
-- GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX
--   ON partsportal.* TO 'partsportal_app'@'127.0.0.1';
-- FLUSH PRIVILEGES;
