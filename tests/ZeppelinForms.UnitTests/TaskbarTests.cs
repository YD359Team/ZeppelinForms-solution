using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.19: who the application is, and its forms' taskbar buttons — progress, badge,
/// attention — as the platform window is told them.</summary>
[Collection("Platform")]
public class TaskbarTests
{
    private static (Form Form, HeadlessWindow Window) Show(Form? form = null)
    {
        form ??= new Form { Size = new Size(300, 200) };

        var window = (HeadlessWindow)new HeadlessPlatform().CreateWindow(form);
        return (form, window);
    }

    [Fact]
    public void WhatWasSetBeforeShowingComesWithTheWindow()
    {
        var form = new Form { Size = new Size(300, 200) };
        form.Taskbar.ProgressValue = 0.4;
        form.Taskbar.Badge = 5;

        var (_, window) = Show(form);

        Assert.Equal((TaskbarProgressState.Normal, 0.4), window.TaskbarProgress);
        Assert.Equal(5, window.TaskbarBadge);
    }

    [Fact]
    public void AValueAsksForTheBarAndStaysInRange()
    {
        var (form, window) = Show();

        form.Taskbar.ProgressValue = 1.7;
        Assert.Equal((TaskbarProgressState.Normal, 1.0), window.TaskbarProgress);

        form.Taskbar.ProgressState = TaskbarProgressState.Error;
        form.Taskbar.ProgressValue = -3;
        Assert.Equal((TaskbarProgressState.Error, 0.0), window.TaskbarProgress);

        form.Taskbar.ProgressState = TaskbarProgressState.Indeterminate;
        Assert.Equal(TaskbarProgressState.Indeterminate, window.TaskbarProgress.State);
    }

    [Fact]
    public void ABadgeOfNothingIsNone()
    {
        var (form, window) = Show();

        form.Taskbar.Badge = 3;
        Assert.Equal(3, window.TaskbarBadge);

        form.Taskbar.Badge = 0;
        Assert.Null(form.Taskbar.Badge);
        Assert.Null(window.TaskbarBadge);

        form.Taskbar.Badge = 2;
        form.Taskbar.ProgressValue = 0.5;
        form.Taskbar.Clear();

        Assert.Null(window.TaskbarBadge);
        Assert.Equal(TaskbarProgressState.None, window.TaskbarProgress.State);
    }

    [Fact]
    public void AttentionIsAskedOnlyOfAWindowInTheBackground()
    {
        var (form, window) = Show();

        form.OnWindowActivated(true);
        form.RequestAttention();
        Assert.Equal(0, window.AttentionRequests);

        form.OnWindowActivated(false);
        form.RequestAttention();
        Assert.Equal(1, window.AttentionRequests);
    }

    [Fact]
    public void ActivateBringsAHiddenFormBackAndForward()
    {
        var (form, window) = Show();
        form.Show();
        form.Hide();

        form.Activate();

        Assert.True(form.Visible);
        Assert.True(window.IsActive);
    }

    [Fact]
    public void ActivateLeavesAFormNeverShown()
    {
        var form = new Form();

        form.Activate();

        Assert.False(form.Visible);
    }

    [Theory]
    [InlineData("ZF Linux", "ZF-Linux")]
    [InlineData("com.example.Orders", "com.example.Orders")]
    [InlineData("  my_app-2 ", "my_app-2")]
    [InlineData("Приложение", "----------")]
    public void AnIdIsMadeFitForTheSystems(string written, string id) =>
        Assert.Equal(id, App.SanitizeId(written));

    [Fact]
    public void TheIdentityComesFromTheEntryAssemblyUntilSet()
    {
        try
        {
            Assert.False(App.IsIdSet);
            Assert.False(string.IsNullOrWhiteSpace(App.Id));
            Assert.False(string.IsNullOrWhiteSpace(App.DisplayName));

            // the SDK's "+commit" is not part of a version people read
            Assert.DoesNotContain("+", App.Version, StringComparison.Ordinal);

            App.Id = "com.example Orders";
            App.Version = "2.1.0-beta";
            App.DisplayName = "Orders";

            Assert.True(App.IsIdSet);
            Assert.Equal("com.example-Orders", App.Id);
            Assert.Equal("2.1.0-beta", App.Version);
            Assert.Equal("Orders", App.DisplayName);
        }
        finally
        {
            App.ResetIdentity();
        }
    }
}
