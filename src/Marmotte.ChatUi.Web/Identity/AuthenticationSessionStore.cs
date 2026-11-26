using System.Security.Claims;
using System.Security.Cryptography;
using Marmotte.ChatUi.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace Marmotte.ChatUi.Web.Identity;

/// <summary>Stores authentication tickets only in this host's memory and revokes HTTP and circuit access together.</summary>
/// <remarks>Singleton for a single-process POC. Restarting the host signs everyone out. Tickets expire absolutely; no sliding renewal is supported.</remarks>
internal sealed class AuthenticationSessionStore(IMemoryCache cache, TimeProvider clock) : ITicketStore
{
    internal const string SessionClaim = "marmotte:session";

    /// <summary>Stores a copy of a validated ticket with a fresh unpredictable session key; throws if identity or expiry is absent.</summary>
    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        if (OidcIdentity.Map(ticket.Principal) is null || ticket.Properties.ExpiresUtc is null)
        {
            throw new InvalidOperationException("A validated identity and absolute expiry are required.");
        }

        var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var identity = (ClaimsIdentity)ticket.Principal.Identity!;
        foreach (var claim in identity.FindAll(SessionClaim).ToArray())
        {
            identity.RemoveClaim(claim);
        }

        identity.AddClaim(new Claim(SessionClaim, key));
        cache.Set(CacheKey(key), TicketSerializer.Default.Serialize(ticket), ticket.Properties.ExpiresUtc.Value);
        return Task.FromResult(key);
    }

    /// <summary>Does not renew an absolute session or resurrect a revoked ticket.</summary>
    public Task RenewAsync(string key, AuthenticationTicket ticket) => Task.CompletedTask;

    /// <summary>Returns an independent ticket snapshot, or null after expiry, revocation, or host restart.</summary>
    public Task<AuthenticationTicket?> RetrieveAsync(string key) => Task.FromResult(Find(key));

    /// <summary>Revokes this session immediately, including every circuit using its principal.</summary>
    public Task RemoveAsync(string key)
    {
        cache.Remove(CacheKey(key));
        return Task.CompletedTask;
    }

    /// <summary>Resolves only an active server ticket whose owner matches the supplied principal; browser owner fields are irrelevant.</summary>
    internal UserId? GetUserId(ClaimsPrincipal principal)
    {
        var key = OidcIdentity.SingleClaim(principal, SessionClaim);
        var ticket = key is null ? null : Find(key);
        var owner = ticket is null ? null : OidcIdentity.Map(ticket.Principal);
        return owner is not null && owner == OidcIdentity.Map(principal) ? owner : null;
    }

    /// <summary>Deserializes a private snapshot and enforces the clock even before memory-cache eviction occurs.</summary>
    private AuthenticationTicket? Find(string key)
    {
        if (!cache.TryGetValue<byte[]>(CacheKey(key), out var bytes) || bytes is null)
        {
            return null;
        }

        var ticket = TicketSerializer.Default.Deserialize(bytes);
        if (ticket?.Properties.ExpiresUtc is not { } expiry || expiry <= clock.GetUtcNow())
        {
            cache.Remove(CacheKey(key));
            return null;
        }

        return ticket;
    }

    /// <summary>Separates session entries from unrelated host cache entries.</summary>
    private static string CacheKey(string key) => $"marmotte-session:{key}";
}
