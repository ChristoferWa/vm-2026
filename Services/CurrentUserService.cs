using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using VmTips.Web.Data;
using VmTips.Web.Models;

namespace VmTips.Web.Services;

public sealed class CurrentUserService(AppDbContext db, IJSRuntime js, ILogger<CurrentUserService> logger)
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
                _cachedUser = await db.Participants.FirstOrDefaultAsync(x => x.Id == participantId);
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
        var participant = await db.Participants.FirstOrDefaultAsync(x => x.AccessCode == normalizedCode);
        if (participant is null)
        {
            logger.LogWarning("Failed login attempt using access code {AccessCode}", normalizedCode);
            return null;
        }

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
}
