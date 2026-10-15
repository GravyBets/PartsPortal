using Microsoft.EntityFrameworkCore;
using PartsPortal.Api.Data;
using PartsPortal.Contracts.Access;
using System.Data;

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
            var active = false;

            await using var command = connection.CreateCommand();

            command.CommandText = """
                SELECT u.is_active, r.code
                FROM app_users u
                LEFT JOIN app_user_roles ur ON ur.user_id = u.id
                LEFT JOIN app_roles r ON r.id = ur.role_id
                WHERE u.employee_id = @employee_id
                ORDER BY r.code;
                """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@employee_id";
            parameter.Value = employeeId;
            command.Parameters.Add(parameter);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                active = reader.GetBoolean(0);

                if (!reader.IsDBNull(1))
                    roles.Add(reader.GetString(1));
            }

            await reader.CloseAsync();

            var bootstrapAvailable =
                !await AnyActiveAdminAsync(connection, cancellationToken);

            var isAdmin =
                active &&
                roles.Contains("ADMIN", StringComparer.OrdinalIgnoreCase);

            var canManageParts =
                active &&
                (isAdmin ||
                 roles.Contains(
                     "PARTS_MANAGER",
                     StringComparer.OrdinalIgnoreCase));

            return new CurrentUserAccessDto(
                employeeId,
                roles.Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x)
                     .ToList(),
                canManageParts,
                isAdmin,
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

    private static async Task<bool> AnyActiveAdminAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM app_user_roles ur
            INNER JOIN app_users u ON u.id = ur.user_id
            INNER JOIN app_roles r ON r.id = ur.role_id
            WHERE u.is_active = 1
              AND r.code = 'ADMIN';
            """;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static string NormalizeEmployeeId(string value)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Employee ID is required.");

        if (normalized.Length > 64)
            throw new ArgumentException(
                "Employee ID cannot exceed 64 characters.");

        return normalized;
    }
}
