using System.Text.Json;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Entities;
using LeadMine.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Services;

public sealed class AuditService(
    LeadMineDbContext db,
    ICurrentUser currentUser,
    ILogger<AuditService> logger) : IAuditService
{
    public async Task LogAsync(
        string action,
        string entityType,
        string? entityId = null,
        bool succeeded = true,
        object? details = null,
        CancellationToken ct = default)
    {
        try
        {
            var entry = new AuditEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                UserId = currentUser.UserId,
                UserName = currentUser.Email ?? "system",
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                IpAddress = currentUser.IpAddress,
                UserAgent = Truncate(currentUser.UserAgent, 512),
                Succeeded = succeeded,
                DetailsJson = details is null ? "{}" : JsonSerializer.Serialize(details),
            };

            db.AuditEntries.Add(entry);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Auditing must never break the operation being audited. The failure
            // still reaches the application log so it is not invisible.
            logger.LogError(ex, "Failed to write audit entry for {Action} on {EntityType}", action, entityType);
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
