using Microsoft.EntityFrameworkCore;
using PartsPortal.Api.Data;
using PartsPortal.Contracts.Access;
using System.Data;
using System.Data.Common;

namespace PartsPortal.Api.Security;

public sealed class AccessRepository
{
    public const string UserHeaderName = "X-PartsPortal-User";

    private readonly PartsPortalDbContext _db;

    public AccessRepository(PartsPortalDbContext db)
    {
        _db = db;
    }

    public async Task<CurrentUserAccessDto> GetCurrentAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        employeeId = NormalizeEmployeeId(employeeId);

        var connection = _db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;

        if (closeWhenDone)
            await connection.OpenAsync(cancellationToken);

        try
        {
            var roles = new List<string>();
            string? displayName = null;
            var active = false;

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT u.display_name, u.is_active, r.code
                    FROM app_users u
                    LEFT JOIN app_user_roles ur ON ur.user_id = u.id
                    LEFT JOIN app_roles r ON r.id = ur.role_id
                    WHERE u.employee_id = @employee_id
                    ORDER BY r.code;
                    """;

                AddParameter(command, "@employee_id", employeeId);

                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    if (!reader.IsDBNull(0))
                        displayName ??= reader.GetString(0);

                    active = reader.GetBoolean(1);

                    if (!reader.IsDBNull(2))
                        roles.Add(reader.GetString(2));
                }
            }

            var bootstrapAvailable =
                !await AnyActiveAdminAsync(
                    connection,
                    transaction: null,
                    cancellationToken);

            roles = roles
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            var canManageUsers =
                active &&
                roles.Contains(
                    "ADMIN",
                    StringComparer.OrdinalIgnoreCase);

            var canManageParts =
                active &&
                (canManageUsers ||
                 roles.Contains(
                     "PARTS_MANAGER",
                     StringComparer.OrdinalIgnoreCase));

            return new CurrentUserAccessDto(
                employeeId,
                displayName,
                roles,
                canManageParts,
                canManageUsers,
                bootstrapAvailable);
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }
    }

    public async Task<bool> CanManagePartsAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
        => (await GetCurrentAsync(employeeId, cancellationToken))
            .CanManageParts;

    public async Task<bool> CanManageUsersAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
        => (await GetCurrentAsync(employeeId, cancellationToken))
            .CanManageUsers;

    public async Task<IReadOnlyList<UserAccessDto>> GetUsersAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = _db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;

        if (closeWhenDone)
            await connection.OpenAsync(cancellationToken);

        try
        {
            var users = new List<UserAccessDto>();

            await using var command = connection.CreateCommand();

            command.CommandText = """
                SELECT
                    u.id,
                    u.employee_id,
                    u.display_name,
                    u.is_active,
                    GROUP_CONCAT(
                        r.code
                        ORDER BY r.code
                        SEPARATOR ','
                    ) AS roles
                FROM app_users u
                LEFT JOIN app_user_roles ur
                    ON ur.user_id = u.id
                LEFT JOIN app_roles r
                    ON r.id = ur.role_id
                GROUP BY
                    u.id,
                    u.employee_id,
                    u.display_name,
                    u.is_active
                ORDER BY u.employee_id;
                """;

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var roleText = reader.IsDBNull(4)
                    ? ""
                    : reader.GetString(4);

                var roles = roleText
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();

                users.Add(new UserAccessDto(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetBoolean(3),
                    roles));
            }

            return users;
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }
    }

    public async Task<UserAccessDto> SaveUserAsync(
        SaveUserAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var employeeId = NormalizeEmployeeId(request.EmployeeId);

        var requestedRoles = (request.Roles ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var validRoles = new HashSet<string>(
            new[] { "ADMIN", "PARTS_MANAGER" },
            StringComparer.OrdinalIgnoreCase);

        var invalidRoles = requestedRoles
            .Where(x => !validRoles.Contains(x))
            .ToList();

        if (invalidRoles.Count > 0)
        {
            throw new ArgumentException(
                "Unknown role(s): " +
                string.Join(", ", invalidRoles));
        }

        var connection = _db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;

        if (closeWhenDone)
            await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            int? existingUserId = null;
            var currentlyAdmin = false;

            await using (var existing = connection.CreateCommand())
            {
                existing.Transaction = transaction;
                existing.CommandText = """
                    SELECT
                        u.id,
                        EXISTS(
                            SELECT 1
                            FROM app_user_roles ur
                            INNER JOIN app_roles r
                                ON r.id = ur.role_id
                            WHERE ur.user_id = u.id
                              AND r.code = 'ADMIN'
                        ) AS is_admin
                    FROM app_users u
                    WHERE u.employee_id = @employee_id;
                    """;

                AddParameter(
                    existing,
                    "@employee_id",
                    employeeId);

                await using var reader =
                    await existing.ExecuteReaderAsync(
                        cancellationToken);

                if (await reader.ReadAsync(cancellationToken))
                {
                    existingUserId = reader.GetInt32(0);
                    currentlyAdmin = reader.GetBoolean(1);
                }
            }

            var willBeAdmin =
                request.IsActive &&
                requestedRoles.Contains(
                    "ADMIN",
                    StringComparer.OrdinalIgnoreCase);

            if (existingUserId.HasValue &&
                currentlyAdmin &&
                !willBeAdmin)
            {
                await using var countCommand =
                    connection.CreateCommand();

                countCommand.Transaction = transaction;
                countCommand.CommandText = """
                    SELECT COUNT(*)
                    FROM app_user_roles ur
                    INNER JOIN app_users u
                        ON u.id = ur.user_id
                    INNER JOIN app_roles r
                        ON r.id = ur.role_id
                    WHERE u.is_active = 1
                      AND r.code = 'ADMIN'
                      AND u.id <> @user_id;
                    """;

                AddParameter(
                    countCommand,
                    "@user_id",
                    existingUserId.Value);

                var otherAdmins = Convert.ToInt32(
                    await countCommand.ExecuteScalarAsync(
                        cancellationToken));

                if (otherAdmins == 0)
                {
                    throw new InvalidOperationException(
                        "You cannot remove or deactivate the final administrator.");
                }
            }

            var userId = await UpsertUserAsync(
                connection,
                transaction,
                employeeId,
                request.DisplayName,
                request.IsActive,
                cancellationToken);

            await using (var clear = connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText =
                    "DELETE FROM app_user_roles WHERE user_id = @user_id;";

                AddParameter(clear, "@user_id", userId);

                await clear.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var roleCode in requestedRoles)
            {
                await using var grant = connection.CreateCommand();

                grant.Transaction = transaction;
                grant.CommandText = """
                    INSERT INTO app_user_roles (user_id, role_id)
                    SELECT @user_id, id
                    FROM app_roles
                    WHERE code = @role_code;
                    """;

                AddParameter(grant, "@user_id", userId);
                AddParameter(grant, "@role_code", roleCode);

                await grant.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }

        var users = await GetUsersAsync(cancellationToken);

        return users.Single(
            x => string.Equals(
                x.EmployeeId,
                employeeId,
                StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> DeleteUserAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        employeeId = NormalizeEmployeeId(employeeId);

        var connection = _db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;

        if (closeWhenDone)
            await connection.OpenAsync(cancellationToken);

        try
        {
            // Lock active administrator rows before checking the target so
            // concurrent deletes cannot both remove the final admin.
            await using var transaction =
                await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            try
            {
                var activeAdminIds = new HashSet<int>();

                await using (var admins = connection.CreateCommand())
                {
                    admins.Transaction = transaction;
                    admins.CommandText = """
                        SELECT u.id
                        FROM app_users u
                        INNER JOIN app_user_roles ur
                            ON ur.user_id = u.id
                        INNER JOIN app_roles r
                            ON r.id = ur.role_id
                        WHERE u.is_active = 1
                          AND r.code = 'ADMIN'
                        FOR UPDATE;
                        """;

                    await using var reader =
                        await admins.ExecuteReaderAsync(cancellationToken);

                    while (await reader.ReadAsync(cancellationToken))
                        activeAdminIds.Add(reader.GetInt32(0));
                }

                int? userId = null;

                await using (var lookup = connection.CreateCommand())
                {
                    lookup.Transaction = transaction;
                    lookup.CommandText = """
                        SELECT id
                        FROM app_users
                        WHERE employee_id = @employee_id
                        FOR UPDATE;
                        """;
                    AddParameter(lookup, "@employee_id", employeeId);

                    var value = await lookup.ExecuteScalarAsync(
                        cancellationToken);

                    if (value is not null && value is not DBNull)
                        userId = Convert.ToInt32(value);
                }

                if (!userId.HasValue)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return false;
                }

                if (activeAdminIds.Contains(userId.Value) &&
                    activeAdminIds.Count == 1)
                {
                    throw new InvalidOperationException(
                        "You cannot delete the final active administrator. " +
                        "Assign another active administrator first.");
                }

                // Only delete this user's PartsPortal access configuration.
                // Saved templates use owner_employee_id rather than a FK
                // to app_users, so their data remains untouched.
                await using (var roles = connection.CreateCommand())
                {
                    roles.Transaction = transaction;
                    roles.CommandText =
                        "DELETE FROM app_user_roles WHERE user_id = @user_id;";
                    AddParameter(roles, "@user_id", userId.Value);
                    await roles.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var user = connection.CreateCommand())
                {
                    user.Transaction = transaction;
                    user.CommandText =
                        "DELETE FROM app_users WHERE id = @user_id;";
                    AddParameter(user, "@user_id", userId.Value);
                    await user.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return true;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }
    }

    private static async Task<int> UpsertUserAsync(
        DbConnection connection,
        DbTransaction transaction,
        string employeeId,
        string? displayName,
        bool isActive,
        CancellationToken cancellationToken)
    {
        displayName = CleanDisplayName(displayName);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;

            command.CommandText = """
                INSERT INTO app_users
                (
                    employee_id,
                    display_name,
                    is_active,
                    created_at_utc,
                    updated_at_utc
                )
                VALUES
                (
                    @employee_id,
                    @display_name,
                    @is_active,
                    UTC_TIMESTAMP(6),
                    UTC_TIMESTAMP(6)
                )
                ON DUPLICATE KEY UPDATE
                    display_name = VALUES(display_name),
                    is_active = VALUES(is_active),
                    updated_at_utc = UTC_TIMESTAMP(6);
                """;

            AddParameter(command, "@employee_id", employeeId);
            AddParameter(command, "@display_name", displayName);
            AddParameter(command, "@is_active", isActive);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText =
            "SELECT id FROM app_users WHERE employee_id = @employee_id;";

        AddParameter(select, "@employee_id", employeeId);

        return Convert.ToInt32(
            await select.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<bool> AnyActiveAdminAsync(
        DbConnection connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            SELECT COUNT(*)
            FROM app_user_roles ur
            INNER JOIN app_users u
                ON u.id = ur.user_id
            INNER JOIN app_roles r
                ON r.id = ur.role_id
            WHERE u.is_active = 1
              AND r.code = 'ADMIN';
            """;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(
                cancellationToken)) > 0;
    }

    private static string NormalizeEmployeeId(string value)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Employee ID is required.");

        if (normalized.Length > 64)
        {
            throw new ArgumentException(
                "Employee ID cannot exceed 64 characters.");
        }

        return normalized;
    }

    private static string? CleanDisplayName(string? value)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return normalized.Length <= 128
            ? normalized
            : normalized[..128];
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
