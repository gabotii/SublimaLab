using System.Net;
using System.Text.Json;
using SublimaLab.IntegrationTests.Support;

namespace SublimaLab.IntegrationTests;

/// <summary>Runs the app with two-step verification required for administrators (the production default).</summary>
public sealed class AdminTwoFactorFactory : SublimaLabFactory
{
    protected override void ConfigureSettings(IDictionary<string, string?> settings) =>
        settings["Auth:RequireAdminTwoFactor"] = "true";
}

public sealed class TwoFactorTests(AdminTwoFactorFactory factory) : IClassFixture<AdminTwoFactorFactory>
{
    [Fact]
    public async Task Administrators_must_use_two_step_verification_end_to_end()
    {
        // 1. Password-only admin sessions can't reach admin endpoints, and are told why.
        var admin = await factory.CreateApiClientAsync();
        var user = await admin.LoginAsync(SublimaLabFactory.AdminEmail, SublimaLabFactory.AdminPassword);
        Assert.True(user.GetProperty("mustEnableTwoFactor").GetBoolean());

        var refused = await admin.SendAsync(HttpMethod.Get, "/api/admin/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("TWO_FACTOR_REQUIRED", (await ApiClient.ReadAsync<JsonElement>(refused, HttpStatusCode.Forbidden)).GetProperty("code").GetString());

        // 2. Setting up an authenticator unlocks the admin in the same session.
        var setup = await admin.PostAsync<JsonElement>("/api/account/two-factor/setup", null);
        var key = setup.GetProperty("sharedKey").GetString()!;
        Assert.StartsWith("otpauth://totp/", setup.GetProperty("authenticatorUri").GetString());
        Assert.Contains("<svg", setup.GetProperty("qrCodeSvg").GetString());

        var wrong = await admin.SendAsync(HttpMethod.Post, "/api/account/two-factor/enable", new { code = "000000" == Totp.Code(key) ? "111111" : "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var enabled = await admin.PostAsync<JsonElement>("/api/account/two-factor/enable", new { code = Totp.Code(key) });
        var recoveryCodes = enabled.GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Equal(10, recoveryCodes.Count);
        await admin.GetAsync<JsonElement>("/api/admin/dashboard");

        var status = await admin.GetAsync<JsonElement>("/api/account/two-factor");
        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.True(status.GetProperty("required").GetBoolean());

        // 3. Administrators can't turn it off.
        var disable = await admin.SendAsync(HttpMethod.Post, "/api/account/two-factor/disable", new { code = Totp.Code(key) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, disable.StatusCode);

        // 4. A new sign-in needs the second step; the password alone grants nothing.
        var next = await factory.CreateApiClientAsync();
        var pending = await next.PostAsync<JsonElement>("/api/auth/login", new { email = SublimaLabFactory.AdminEmail, password = SublimaLabFactory.AdminPassword, rememberMe = false });
        Assert.True(pending.GetProperty("requiresTwoFactor").GetBoolean());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("user").ValueKind);
        next.UseAntiforgeryToken(pending.GetProperty("antiforgery").GetProperty("token").GetString()!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await next.SendAsync(HttpMethod.Get, "/api/admin/dashboard")).StatusCode);

        var badCode = await next.SendAsync(HttpMethod.Post, "/api/auth/login/2fa", new { code = "12345", rememberMe = false, isRecoveryCode = false });
        Assert.Equal(HttpStatusCode.BadRequest, badCode.StatusCode);

        var signedIn = await next.PostAsync<JsonElement>("/api/auth/login/2fa", new { code = Totp.Code(key, stepOffset: 1), rememberMe = false, isRecoveryCode = false });
        next.UseAntiforgeryToken(signedIn.GetProperty("antiforgery").GetProperty("token").GetString()!);
        Assert.False(signedIn.GetProperty("user").GetProperty("mustEnableTwoFactor").GetBoolean());
        await next.GetAsync<JsonElement>("/api/admin/dashboard");

        // 5. Recovery codes work once, typed in lower case and without the dash.
        var typed = recoveryCodes[0].Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        var recovered = await SignInWithRecoveryCodeAsync(typed);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SignInWithRecoveryCodeAsync(recoveryCodes[0])).StatusCode);

        var after = await next.GetAsync<JsonElement>("/api/account/two-factor");
        Assert.Equal(9, after.GetProperty("recoveryCodesLeft").GetInt32());
    }

    [Fact]
    public async Task Customers_can_turn_two_step_verification_on_and_off()
    {
        var (customer, email, _) = await Scenario.SignedInCustomerAsync(factory);
        var initial = await customer.GetAsync<JsonElement>("/api/account/two-factor");
        Assert.False(initial.GetProperty("enabled").GetBoolean());
        Assert.False(initial.GetProperty("required").GetBoolean());

        var key = (await customer.PostAsync<JsonElement>("/api/account/two-factor/setup", null)).GetProperty("sharedKey").GetString()!;
        await customer.PostAsync<JsonElement>("/api/account/two-factor/enable", new { code = Totp.Code(key) });
        Assert.Contains("on", factory.Emails.Last(email).Subject, StringComparison.Ordinal);

        var regenerated = await customer.PostAsync<JsonElement>("/api/account/two-factor/recovery-codes", new { code = Totp.Code(key, stepOffset: 1) });
        Assert.Equal(10, regenerated.GetProperty("recoveryCodes").GetArrayLength());

        var disabled = await customer.SendAsync(HttpMethod.Post, "/api/account/two-factor/disable", new { code = Totp.Code(key, stepOffset: -1) });
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);

        // Signing in again needs only the password.
        var fresh = await factory.CreateApiClientAsync();
        var user = await fresh.LoginAsync(email, Scenario.Password);
        Assert.False(user.GetProperty("twoFactorEnabled").GetBoolean());
    }

    private async Task<HttpResponseMessage> SignInWithRecoveryCodeAsync(string code)
    {
        var client = await factory.CreateApiClientAsync();
        var pending = await client.PostAsync<JsonElement>("/api/auth/login", new { email = SublimaLabFactory.AdminEmail, password = SublimaLabFactory.AdminPassword, rememberMe = false });
        client.UseAntiforgeryToken(pending.GetProperty("antiforgery").GetProperty("token").GetString()!);
        return await client.SendAsync(HttpMethod.Post, "/api/auth/login/2fa", new { code, rememberMe = false, isRecoveryCode = true });
    }
}
