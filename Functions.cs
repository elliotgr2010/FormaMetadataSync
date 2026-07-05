using Autodesk.Authentication.Model;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AccC3DMetadata
{
    /// <summary>
    /// Base class for AutoCAD command classes.  Provides shared access to the active document
    /// and editor, and owns the 3-legged OAuth flow used to obtain ACC access tokens.
    /// </summary>
    public class Functions
    {
        /// <summary>Gets the currently active AutoCAD MDI document.</summary>
        protected static Document AcadDoc => Application.DocumentManager.MdiActiveDocument;

        /// <summary>Gets the <see cref="Editor"/> for the active document.</summary>
        protected static Editor Ed => AcadDoc.Editor;

        /// <summary>
        /// OAuth redirect URI used for the loopback PKCE auth flow.
        /// Protected so that a subclass can substitute a different port if 8080 is unavailable.
        /// </summary>
        protected static string _redirUrl = ClientConfig.RedirectUri;

        // ── PKCE helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Generates a cryptographically random PKCE code verifier (32 bytes, base64url-encoded).
        /// </summary>
        /// <returns>A 43-character base64url string suitable for use as an OAuth code verifier.</returns>
        private static string GenerateCodeVerifier()
        {
            var bytes = new byte[32]; // 32 bytes → 43-char base64url; within the RFC 7636 43-128 char range.
            RandomNumberGenerator.Fill(bytes);
            return Base64UrlEncode(bytes);
        }

        /// <summary>
        /// Encodes a byte array as base64url (RFC 4648 §5): replaces <c>+</c> with <c>-</c>,
        /// <c>/</c> with <c>_</c>, and strips the <c>=</c> padding.
        /// </summary>
        private static string Base64UrlEncode(byte[] input) =>
            Convert.ToBase64String(input)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');

        // ── Authentication ─────────────────────────────────────────────────────────

        /// <summary>
        /// Performs a 3-legged OAuth PKCE authorization flow and returns a token on success.
        /// </summary>
        /// <remarks>
        /// Flow:
        /// <list type="number">
        ///   <item><description>Generate a PKCE code verifier and its SHA-256 code challenge.</description></item>
        ///   <item><description>Delegate to <see cref="AuthService"/> which opens the system browser,
        ///     listens for the loopback redirect, and exchanges the code for tokens.</description></item>
        /// </list>
        /// Callers should prefer <see cref="Services.TokenCache.GetAccessTokenAsync"/> which
        /// caches the token and silently refreshes it — this method is for the initial acquisition only.
        /// </remarks>
        /// <returns>
        /// A <see cref="ThreeLeggedToken"/> on success, or <c>null</c> if the client ID is
        /// missing or the auth flow throws an exception.
        /// </returns>
        public static async Task<ThreeLeggedToken> Get3LeggedTokenAsync()
        {
            string clientId = ClientConfig.ClientId;
            if (string.IsNullOrWhiteSpace(clientId))
            {
                Ed.WriteMessage("\nUNABLE TO OBTAIN CLIENT ID");
                return null;
            }

            // Generate the PKCE pair. The verifier is a random secret; the challenge is its
            // SHA-256 hash in base64url form. The verifier is sent only during the token exchange
            // (not in the browser redirect) so it never travels through the browser.
            string codeVerifier = GenerateCodeVerifier();
            byte[] challengeBytes;
            using (var sha256 = SHA256.Create())
                challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
            string codeChallenge = Base64UrlEncode(challengeBytes);

            try
            {
                Ed.WriteMessage("\nStarting interactive 3-legged auth via AuthService...");
                var authService = new AuthService();
                ThreeLeggedToken token = await authService
                    .GetThreeLeggedTokenAsync(clientId, codeChallenge, codeVerifier, _redirUrl)
                    .ConfigureAwait(false);

                Ed.WriteMessage(token != null
                    ? "\nAuthentication succeeded."
                    : "\nAuthentication returned a null token.");
                return token;
            }
            catch (Exception ex)
            {
                Ed.WriteMessage($"\nAuthentication failed: {ex.Message}");
                return null;
            }
        }
    }
}
