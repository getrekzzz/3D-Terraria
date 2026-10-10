using System.Collections.Generic;
using System.Linq;
using Strata.Core.Content.Loading;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Presentation;

public class InputConfigTests
{
    private static InputConfig Shipped() => InputConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));

    [Fact]
    public void ShippedBindingsPassTheChecks()
    {
        var config = Shipped();
        Assert.Empty(config.Validate());
        // The whole game is reachable on a controller.
        foreach (var action in InputConfig.PadRequired) Assert.Contains(config.Bindings(action), b => b.IsPad);
        // The hotbar steps on the shoulder buttons, the item is used with the right trigger.
        Assert.Contains(config.Bindings("hotbar_next"), b => b.ToString() == "pad:rb");
        Assert.Contains(config.Bindings("hotbar_prev"), b => b.ToString() == "pad:lb");
        Assert.Contains(config.Bindings("use_primary"), b => b.ToString() == "axis:rt+");
    }

    [Theory]
    [InlineData("key:W", BindingKind.Key, 0, 0)]
    [InlineData("key:Kp Enter", BindingKind.Key, 0, 0)]
    [InlineData("mouse:4", BindingKind.Mouse, 4, 0)]
    [InlineData("pad:a", BindingKind.PadButton, 0, 0)]
    [InlineData("pad:rb", BindingKind.PadButton, 10, 0)]
    [InlineData("pad:dpad_left", BindingKind.PadButton, 13, 0)]
    [InlineData("axis:left_y-", BindingKind.PadAxis, 1, -1)]
    [InlineData("axis:right_x+", BindingKind.PadAxis, 2, 1)]
    [InlineData("axis:rt+", BindingKind.PadAxis, 5, 1)]
    public void ParsesEveryKindAndRoundTrips(string text, BindingKind kind, int code, int sign)
    {
        Assert.True(InputBinding.TryParse(text, out var b, out var error), error);
        Assert.Equal(kind, b.Kind);
        Assert.Equal(code, b.Code);
        Assert.Equal(sign, b.Sign);
        Assert.Equal(text, b.ToString());
    }

    [Theory]
    [InlineData("W")]
    [InlineData("key:")]
    [InlineData("key: W")]
    [InlineData("mouse:0")]
    [InlineData("mouse:12")]
    [InlineData("pad:z")]
    [InlineData("joy:a")]
    [InlineData("axis:left_x")]
    [InlineData("axis:rt-")]
    [InlineData("axis:tilt+")]
    public void RejectsMalformedBindings(string text)
    {
        Assert.False(InputBinding.TryParse(text, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void ControllerNamesReadAsTheButtonsDo()
    {
        Assert.Equal("A", InputBinding.Parse("pad:a").PadLabel);
        Assert.Equal("RB", InputBinding.Parse("pad:rb").PadLabel);
        Assert.Equal("LT", InputBinding.Parse("axis:lt+").PadLabel);
        Assert.Equal("Left Stick Up", InputBinding.Parse("axis:left_y-").PadLabel);
        Assert.Equal("Right Stick Right", InputBinding.Parse("axis:right_x+").PadLabel);
        Assert.Null(InputBinding.Parse("key:W").PadLabel);
    }

    [Fact]
    public void OneControllerButtonCannotDoTwoGameplayThings()
    {
        var actions = Shipped().Actions.ToDictionary(p => p.Key, p => p.Value);
        actions["grapple"] = new[] { "key:F", "pad:a" }; // A already jumps
        var problems = new InputConfig { Actions = actions }.Validate();
        Assert.Contains(problems, p => p.Contains("pad:a") && p.Contains("jump") && p.Contains("grapple"));
    }

    [Fact]
    public void ScreenActionsMayReuseGameplayButtons()
    {
        // ui_accept (A on a screen) and jump (A in the world) share a button: a screen takes the controller over.
        var config = Shipped();
        Assert.Contains(config.Bindings("ui_accept"), b => b.ToString() == "pad:a");
        Assert.Contains(config.Bindings("jump"), b => b.ToString() == "pad:a");
        Assert.True(InputConfig.IsScreenAction("ui_accept"));
        Assert.False(InputConfig.IsScreenAction("jump"));
    }

    [Fact]
    public void MissingActionsAndControllerGapsAreReported()
    {
        var actions = Shipped().Actions.ToDictionary(p => p.Key, p => p.Value);
        actions.Remove("look_left");
        actions["jump"] = new[] { "key:Space" };
        actions["sprint"] = new[] { "key:Shift", "key:Shift" };
        actions["Bad Name"] = new[] { "key:B" };
        var problems = new InputConfig { Actions = actions }.Validate();
        Assert.Contains("missing action look_left", problems);
        Assert.Contains("jump has no controller binding", problems);
        Assert.Contains(problems, p => p.Contains("sprint lists key:Shift twice"));
        Assert.Contains(problems, p => p.Contains("'Bad Name' must be snake_case"));
    }

    [Fact]
    public void OptionalActionsMayShipUnboundButRequiredOnesMayNot()
    {
        var actions = Shipped().Actions.ToDictionary(p => p.Key, p => p.Value);
        Assert.Empty(actions["housing"]); // the inventory's Housing tab opens it; the player may bind a key
        actions["interact"] = System.Array.Empty<string>();
        var problems = new InputConfig { Actions = actions }.Validate();
        Assert.Contains("interact has no bindings", problems);
        Assert.DoesNotContain(problems, p => p.StartsWith("housing"));
    }

    [Fact]
    public void GameplayKeysAreNotSharedExceptChatAndThePhotoShot()
    {
        // Every gameplay key does one thing; Enter opens the chat, and takes the picture only inside photo mode.
        var owners = new Dictionary<string, string>();
        var clashes = new List<string>();
        foreach (var (action, list) in Shipped().Actions)
        {
            if (InputConfig.IsScreenAction(action)) continue;
            foreach (var b in list)
                if (b.StartsWith("key:") && !owners.TryAdd(b, action)) clashes.Add($"{b}: {owners[b]} and {action}");
        }
        Assert.Equal(new[] { "key:Enter: chat and photo_shot" }, clashes);
    }

    [Fact]
    public void PadTuningIsRangeChecked()
    {
        var config = new InputConfig
        {
            Actions = Shipped().Actions,
            Pad = new PadConfig { LookSpeed = 5, Deadzone = 0.6f, AimAssist = new() { Friction = 0, ConeDegrees = 90 } },
        };
        var problems = config.Validate();
        Assert.Contains(problems, p => p.StartsWith("pad.lookSpeed"));
        Assert.Contains(problems, p => p.StartsWith("pad.deadzone"));
        Assert.Contains(problems, p => p.StartsWith("pad.aimAssist.friction"));
        Assert.Contains(problems, p => p.StartsWith("pad.aimAssist.coneDegrees"));
    }
}
