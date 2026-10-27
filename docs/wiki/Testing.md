# Testing

## The headless platform

`HeadlessPlatform` runs forms without a screen: layout, input, focus, styles, bindings and
the dispatcher all work as in a window, so a form is tested as a user would drive it.

```csharp
[Fact]
public void TheGreetingNamesTheUser()
{
    var platform = new HeadlessPlatform();
    var form = new MainForm { Size = new Size(480, 320) };

    platform.CreateWindow(form);
    form.UpdateLayout();

    form.NameBox.Focus();
    HeadlessInput.TypeText(form, "Zeppelin");
    HeadlessInput.PressKey(form, Key.Enter);

    platform.PumpAll();                    // run what was queued to the dispatcher

    Assert.Equal("Hello, Zeppelin!", form.Greeting.Text);
}
```

| Member | |
| ------ | - |
| `platform.CreateWindow(form)` | Opens the form in a window of the given size |
| `form.UpdateLayout()` | Runs the pending layout now |
| `platform.Pump()`, `PumpAll()` | Runs one or all of the dispatcher's queued actions — `BeginInvoke`, timer ticks, changes of a data source from another thread |
| `HeadlessInput.Click`, `DoubleClick`, `RightClick`, `MoveMouse` | The mouse, at form coordinates |
| `HeadlessInput.PressKey(form, key, modifiers)` | A whole chord: pressed and released, the modifiers let go too |
| `HeadlessInput.TypeText`, `Compose`, `CommitComposition` | Typing and input-method composition |
| `HeadlessInput.Tap`, `Swipe`, `TouchDown/Move/Up` | Touch, with flings |

Element positions for the mouse come from the layout: `element.GetAbsolutePosition()` and
`element.ActualSize`.

The headless window records what the form asked of it — `HeadlessWindow.IsTopMost`,
`MinimumSizeLimit`, `MaximumSizeLimit`, `CenterOnScreenCount` — so window behaviour is tested
too.

A file dialog is answered by the test: `HeadlessPlatform.NativeFileDialog` plays the system's
dialog, given what it was asked and returning the paths chosen — an empty array for a cancel.

```csharp
platform.NativeFileDialog = (mode, options) => [Path.Combine(dataFolder, "orders.csv")];

Point at = form.ImportButton.GetAbsolutePosition();
HeadlessInput.Click(form, at.X + 4, at.Y + 4);   // FileDialog.OpenFile inside gets the file
```

Tests that open headless windows share the process-wide state of the UI thread and the
keyboard: run them in one xUnit collection, not in parallel.

## Drawing

`ElementTreeRenderer.Draw(element, graphics)` draws a tree into any `Graphics`. A
`HeadlessGraphics` subclass that records the calls checks what was drawn without pixels:

```csharp
private sealed class FillRecorder : HeadlessGraphics
{
    public List<Color> Fills { get; } = [];

    public override void FillRectangle(Rectangle rect, Color color) => Fills.Add(color);
}
```

`element.RenderToImage()` renders the element into an image where a renderer is registered.

## Snapshots

The framework's own visual tests compare renders with reference images in
`tests/ZeppelinForms.UnitTests/Snapshots/Expected/{win,linux}` — text rendering differs
between the platforms, so each has its set.

- bash: `ZF_UPDATE_SNAPSHOTS=true dotnet test`
- PowerShell: `$env:ZF_UPDATE_SNAPSHOTS='true'; dotnet test`

`ZF_CREATE_SNAPSHOTS=true` only adds the missing ones.

## Benchmarks

`bench/ZeppelinForms.Benchmarks` measures layout, rendering, text and the scrolling of a
virtualized list against a stored baseline:

```powershell
dotnet run --project bench/ZeppelinForms.Benchmarks -c Release
dotnet run --project bench/ZeppelinForms.Benchmarks -c Release -- --update-baseline
```

Run-to-run spread on a desktop reaches tens of percent: take the median of three, and record
a baseline only on a quiet machine.
