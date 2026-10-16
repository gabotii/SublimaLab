using FluentValidation;

namespace SublimaLab.Application.Accounts;

internal static class PasswordRules
{
    public const int MinLength = 10;

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(MinLength).WithMessage($"Password must be at least {MinLength} characters long.")
            .MaximumLength(128)
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a number.");
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256).EmailAddress().WithMessage("A valid email address is required.");
        RuleFor(r => r.Password).StrongPassword();
        RuleFor(r => r.FirstName).NotEmpty().MaximumLength(60);
        RuleFor(r => r.LastName).NotEmpty().MaximumLength(60);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class ConfirmEmailRequestValidator : AbstractValidator<ConfirmEmailRequest>
{
    public ConfirmEmailRequestValidator()
    {
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.Token).NotEmpty().MaximumLength(2048);
    }
}

public sealed class EmailRequestValidator : AbstractValidator<EmailRequest>
{
    public EmailRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256).EmailAddress().WithMessage("A valid email address is required.");
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(r => r.Token).NotEmpty().MaximumLength(2048);
        RuleFor(r => r.NewPassword).StrongPassword();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(r => r.NewPassword).StrongPassword().NotEqual(r => r.CurrentPassword).WithMessage("The new password must be different.");
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(r => r.FirstName).NotEmpty().MaximumLength(60);
        RuleFor(r => r.LastName).NotEmpty().MaximumLength(60);
        RuleFor(r => r.PhoneNumber).MaximumLength(30).Matches(@"^\+?[0-9 ()\-.]{6,30}$").When(r => !string.IsNullOrEmpty(r.PhoneNumber));
    }
}
