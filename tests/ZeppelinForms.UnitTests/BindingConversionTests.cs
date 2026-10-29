using System.Globalization;
using Xunit;
using ZeppelinForms.Data;

namespace ZeppelinForms.UnitTests;

/// <summary>0.19.1: the conversions of a binding, made once per pair of types — the
/// results the per-write ChangeType gave, and a few it couldn't give.</summary>
public class BindingConversionTests
{
    private enum Color { Red, Green }

    private enum Shade { Green, Red }

    private static object? Convert(object value, Type target) =>
        BindingConversion.For(value.GetType(), target)(value);

    [Fact]
    public void AValueOfTheTypeIsNotConverted()
    {
        Assert.Same(BindingConversion.Identity, BindingConversion.For(typeof(string), typeof(object)));
        Assert.Same(BindingConversion.Identity, BindingConversion.For(typeof(int), typeof(int?)));
    }

    [Fact]
    public void TheConversionIsMadeOncePerPair() =>
        Assert.Same(BindingConversion.For(typeof(string), typeof(int)), BindingConversion.For(typeof(string), typeof(int)));

    [Fact]
    public void AnEnumTakesANumberAsTheMembersValueAndTextAsAName()
    {
        Assert.Equal(Color.Green, Convert(1, typeof(Color)));
        Assert.Equal(Color.Green, Convert("Green", typeof(Color)));
        Assert.Equal(Color.Green, Convert("1", typeof(Color?)));
    }

    [Fact]
    public void AnotherEnumGoesByName() =>
        Assert.Equal(Shade.Red, Convert(Color.Red, typeof(Shade)));

    [Fact]
    public void TextIsReadInTheCultureOfTheMomentNotOfTheFirstCall()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            Assert.Equal(1.5, Convert("1,5", typeof(double)));

            // the same cached conversion, the new culture
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(1.5, Convert("1.5", typeof(double)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void WhatHasNoIConvertibleStillBecomesText()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id.ToString(), Convert(id, typeof(string)));
        Assert.Equal("00:01:00", Convert(TimeSpan.FromMinutes(1), typeof(string)));
    }

    [Fact]
    public void AValueThatDoesNotFitThrowsWhatTheBindingCatches()
    {
        Assert.Throws<FormatException>(() => Convert("-", typeof(int)));
        Assert.Throws<OverflowException>(() => Convert(long.MaxValue, typeof(int)));
        Assert.Throws<ArgumentException>(() => Convert("Blue", typeof(Color)));
        Assert.Throws<InvalidCastException>(() => Convert(new object(), typeof(Version)));
    }
}