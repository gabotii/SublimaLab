namespace SublimaLab.Blazor.Models;

public sealed record LoginRequest(string Email, string Password, bool RememberMe);

public sealed record TwoFactorLoginRequest(string Code, bool RememberMe, bool IsRecoveryCode);

public sealed record TwoFactorCodeRequest(string Code);

public sealed record RegisterRequest(string Email, string Password, string FirstName, string LastName);

public sealed record ConfirmEmailRequest(Guid UserId, string Token);

public sealed record EmailRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UpdateProfileRequest(string FirstName, string LastName, string? PhoneNumber);

public sealed record SaveAddressRequest(
    string? Label,
    string RecipientName,
    string Phone,
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string Country,
    bool IsDefault);

public sealed record ApplyDiscountRequest(string Code);

public sealed record CreatePaymentRequest(Guid OrderId);

public sealed record ConfirmPaymentRequest(string? PaymentToken);
