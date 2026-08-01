using System.ComponentModel.DataAnnotations;

namespace LeadMine.Application.Validation;

/// <summary>
/// Validates an email address but treats null and empty as valid.
/// <para>
/// The built-in <see cref="EmailAddressAttribute"/> passes null yet rejects an
/// empty string, which breaks two ordinary cases: a lead discovered without an
/// email, and a client clearing an existing one by sending "". Both are legal
/// here, so only a non-blank value is actually checked.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class OptionalEmailAddressAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute Inner = new();

    public OptionalEmailAddressAttribute()
        : base("The {0} field is not a valid e-mail address.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        if (value is not string text) return false;

        return string.IsNullOrWhiteSpace(text) || Inner.IsValid(text);
    }
}
