using System.Buffers.Binary;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Web.Identity;

/// <summary>Maps an already validated OIDC principal to a versioned, stable owner identifier without using email or display names.</summary>
internal static class OidcIdentity
{
    /// <summary>Returns null for anonymous, missing, or ambiguous identity claims; callers must validate the token before mapping.</summary>
    internal static UserId? Map(ClaimsPrincipal principal)
    {
        var identities = principal.Identities.ToArray();
        if (identities.Length != 1 || !identities[0].IsAuthenticated)
        {
            return null;
        }

        var issuer = SingleClaim(principal, "iss");
        var subject = SingleClaim(principal, "sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        // Length-prefixed UTF-8 preserves exact case and boundaries without serializer-dependent escaping.
        var issuerBytes = Encoding.UTF8.GetBytes(issuer);
        var subjectBytes = Encoding.UTF8.GetBytes(subject);
        var pair = new byte[8 + issuerBytes.Length + subjectBytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(pair, issuerBytes.Length);
        issuerBytes.CopyTo(pair, 4);
        BinaryPrimitives.WriteInt32BigEndian(pair.AsSpan(4 + issuerBytes.Length), subjectBytes.Length);
        subjectBytes.CopyTo(pair, 8 + issuerBytes.Length);
        return new UserId($"oidc-v1:{Convert.ToHexStringLower(SHA256.HashData(pair))}");
    }

    /// <summary>Rejects duplicate claims instead of choosing an arbitrary identity or session.</summary>
    internal static string? SingleClaim(ClaimsPrincipal principal, string type)
    {
        var claims = principal.FindAll(type).Take(2).ToArray();
        return claims.Length == 1 ? claims[0].Value : null;
    }
}
