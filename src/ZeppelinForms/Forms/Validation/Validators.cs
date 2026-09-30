using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ZeppelinForms.Core.Globalization;

namespace ZeppelinForms.Forms.Validation;

/// <remarks>
/// A null message means the localized default. It is resolved when validation
/// runs, not when the validator is created, so a language switch applies to the
/// next check without rebuilding the validators.
/// </remarks>
public static class Validators
{
    public static Func<string, string?> Required(string? message = null) =>
        value => string.IsNullOrWhiteSpace(value) ? message ?? Localization.Get(ZfText.Required) : null;

    public static Func<string, string?> Email(string? message = null) =>
        value => string.IsNullOrEmpty(value) || EmailPattern.IsMatch(value)
            ? null
            : message ?? Localization.Get(ZfText.InvalidEmail);

    public static Func<string, string?> Length(int min, int max) =>
        value => value.Length < min ? Localization.Get(ZfText.TooShort, min)
            : value.Length > max ? Localization.Get(ZfText.TooLong, max)
            : null;

    public static Func<string, string?> Digits(string? message = null) =>
        value => value.All(char.IsAsciiDigit) ? null : message ?? Localization.Get(ZfText.DigitsOnly);

    public static Func<string, string?> Combine(params Func<string, string?>[] validators) =>
        value =>
        {
            foreach (Func<string, string?> validator in validators)
                if (validator(value) is string error)
                    return error;

            return null;
        };

    private static readonly Regex EmailPattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
}