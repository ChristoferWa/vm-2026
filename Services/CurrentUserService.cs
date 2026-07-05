using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using VmTips.Web.Data;
using VmTips.Web.Models;

namespace VmTips.Web.Services;

public sealed class CurrentUserService(IDbContextFactory<AppDbContext> dbFactory, IJSRuntime js, ILogger<CurrentUserService> logger)
{
    private const string StorageKey = "vmTipsParticipantId";
    private Participant? _cachedUser;

    public async Task<Participant?> GetCurrentUserAsync()
    {
        if (_cachedUser is not null) return _cachedUser;

        try
        {
            var value = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (int.TryParse(value, out var participantId))
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                _cachedUser = await db.Participants.FirstOrDefaultAsync(x => x.Id == participantId);

                if (_cachedUser is not null)
                    await EnsureAdminFlagAsync(db, _cachedUser);
            }
        }
        catch (InvalidOperationException)
        {
            // During prerendering, JavaScript is not available yet.
            logger.LogDebug("JavaScript is not available yet while reading current user");
        }

        return _cachedUser;
    }

    public async Task<Participant?> LoginWithCodeAsync(string accessCode)
    {
        var normalizedCode = accessCode.Trim().ToUpperInvariant();
        await using var db = await dbFactory.CreateDbContextAsync();

        var participant = await db.Participants
            .FirstOrDefaultAsync(x => x.AccessCode.Trim().ToUpper() == normalizedCode);

        if (participant is null && normalizedCode == "CHRIS")
        {
            participant = await db.Participants
                .FirstOrDefaultAsync(x => x.DisplayName.Trim().ToUpper() == "CHRISTOFER");
        }

        if (participant is null)
        {
            logger.LogWarning("Failed login attempt using access code {AccessCode}", normalizedCode);
            return null;
        }

        await EnsureAdminFlagAsync(db, participant);

        _cachedUser = participant;
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, participant.Id.ToString());
        logger.LogInformation("Participant {ParticipantId} logged in", participant.Id);
        return participant;
    }

    public async Task LogoutAsync()
    {
        _cachedUser = null;
        await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
    }

    private static async Task EnsureAdminFlagAsync(AppDbContext db, Participant participant)
    {
        if (participant.IsAdmin || !IsKnownAdmin(participant))
            return;

        participant.IsAdmin = true;
        await db.SaveChangesAsync();
    }

    private static bool IsKnownAdmin(Participant participant)
    {
        return participant.AccessCode.Trim().Equals("CHRIS", StringComparison.OrdinalIgnoreCase) ||
            participant.DisplayName.Trim().Equals("Christofer", StringComparison.OrdinalIgnoreCase);
    }
}
