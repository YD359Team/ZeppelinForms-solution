using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ZeppelinForms.Forms.Validation;

public static class Validators
{
    public static Func<string, string?> Required(string message = "Поле обязательно") =>
        value => string.IsNullOrWhiteSpace(value) ? message : null;

    public static Func<string, string?> Email(string message = "Некорректный адрес") =>
        value => string.IsNullOrEmpty(value) || EmailPattern.IsMatch(value) ? null : message;

    public static Func<string, string?> Length(int min, int max) =>
        value => value.Length < min ? $"Не короче {min} символов"
            : value.Length > max ? $"Не длиннее {max} символов"
            : null;

    public static Func<string, string?> Digits(string message = "Только цифры") =>
        value => value.All(char.IsAsciiDigit) ? null : message;

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
