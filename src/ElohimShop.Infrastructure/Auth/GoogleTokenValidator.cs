using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElohimShop.Application.Auth;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ElohimShop.Infrastructure.Auth;

public sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleTokenValidator>? _logger;

    public GoogleTokenValidator(
        IConfiguration configuration,
        HttpClient? httpClient = null,
        ILogger<GoogleTokenValidator>? logger = null)
    {
        _configuration = configuration;
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger;
    }

    public async Task<GoogleTokenPayload> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")
            ?? _configuration["GOOGLE_CLIENT_ID"]
            ?? _configuration["Google:ClientId"];

        if (string.IsNullOrWhiteSpace(clientId))
        {
            _logger?.LogError("Google OAuth client ID not configured.");
            throw new InvalidOperationException("Google OAuth no está configurado en el servidor.");
        }

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            };
            var payload = await GoogleJsonWebSignature.ValidateAsync(token, settings)
                .WaitAsync(cancellationToken);

            return new GoogleTokenPayload(
                payload.Subject,
                payload.Email,
                payload.Name ?? payload.Email,
                payload.EmailVerified,
                payload.Picture);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidJwtException or ArgumentException)
        {
            _logger?.LogWarning(ex, "JWT validation failed for token. Attempting Google UserInfo fallback.");

            // Fallback: Check if token is a Google OAuth2 access token
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var userInfo = await response.Content.ReadFromJsonAsync<GoogleUserInfoResponse>(options, cancellationToken: cancellationToken);
                    if (userInfo != null && !string.IsNullOrWhiteSpace(userInfo.Email))
                    {
                        return new GoogleTokenPayload(
                            userInfo.Sub,
                            userInfo.Email,
                            userInfo.Name ?? userInfo.Email,
                            userInfo.EmailVerified ?? true,
                            userInfo.Picture);
                    }
                }
                else
                {
                    var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger?.LogError("Google UserInfo API returned status {Status}: {Body}", response.StatusCode, errBody);
                }
            }
            catch (Exception fallbackEx)
            {
                _logger?.LogError(fallbackEx, "Failed during Google UserInfo fallback call.");
            }

            throw new UnauthorizedAccessException("El token de Google es inválido o expiró.", ex);
        }
    }

    private sealed record GoogleUserInfoResponse(
        [property: JsonPropertyName("sub")] string Sub,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("email_verified")] bool? EmailVerified,
        [property: JsonPropertyName("picture")] string? Picture
    );
}


