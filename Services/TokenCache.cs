using Autodesk.Authentication.Model;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AccC3DMetadata.Services
{
    /// <summary>
    /// Thread-safe session-scoped cache for the 3-legged OAuth token.
    /// </summary>
    /// <remarks>
    /// All operations that need an ACC access token should call
    /// <see cref="GetAccessTokenAsync"/> rather than triggering auth directly. The cache:
    /// <list type="bullet">
    ///   <item><description>Returns the cached token when it has more than 5 minutes of lifetime remaining.</description></item>
    ///   <item><description>Silently refreshes using the refresh token when expiry is within 5 minutes.</description></item>
    ///   <item><description>Falls back to a full browser-based PKCE flow when no valid token or refresh token exists.</description></item>
    /// </list>
    /// A <see cref="SemaphoreSlim"/> with a count of 1 serialises concurrent callers so that
    /// only one auth flow runs at a time — this prevents multiple browser windows from opening
    /// if several commands fire in rapid succession.
    /// </remarks>
    internal static class TokenCache
    {
        private static ThreeLeggedToken _token;

        /// <summary>
        /// Semaphore that ensures only one caller at a time can enter the token-acquisition logic.
        /// Using SemaphoreSlim rather than lock because the body contains awaits.
        /// </summary>
        private static readonly SemaphoreSlim _lock = new(1, 1);

        /// <summary>
        /// Returns a valid access token string, refreshing or re-authenticating as needed.
        /// </summary>
        /// <returns>
        /// The access token string on success, or <c>null</c> if authentication could not be completed.
        /// </returns>
        public static async Task<string> GetAccessTokenAsync()
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Return the cached token if it has more than 5 minutes remaining.
                // ExpiresAt is a Unix epoch timestamp (long seconds).
                if (_token != null && _token.ExpiresAt.HasValue &&
                    DateTimeOffset.FromUnixTimeSeconds(_token.ExpiresAt.Value).UtcDateTime
                        > DateTime.UtcNow.AddMinutes(5))
                    return _token.AccessToken;

                // Attempt a silent refresh if a refresh token is available.
                if (_token?.RefreshToken != null)
                {
                    try
                    {
                        var authSvc = new AuthService();
                        _token = await authSvc.RefreshAsync(ClientConfig.ClientId, _token.RefreshToken)
                            .ConfigureAwait(false);
                        if (_token != null)
                            return _token.AccessToken;
                    }
                    catch
                    {
                        // Refresh failed (e.g. token revoked or session expired).
                        // Clear the stale token and fall through to the full browser flow.
                        _token = null;
                    }
                }

                // Full browser-based PKCE authentication flow.
                _token = await Functions.Get3LeggedTokenAsync().ConfigureAwait(false);
                return _token?.AccessToken;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Clears the cached token, forcing the next call to <see cref="GetAccessTokenAsync"/>
        /// to perform a full re-authentication. Called by <see cref="PluginEntry.Terminate"/>
        /// when AutoCAD unloads the plugin.
        /// </summary>
        public static void Invalidate()
        {
            _lock.Wait(); // Synchronous wait — safe to call from non-async Terminate().
            try { _token = null; }
            finally { _lock.Release(); }
        }
    }
}
