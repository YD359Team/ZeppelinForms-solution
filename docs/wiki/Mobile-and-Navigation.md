# Mobile and navigation

The frame of a phone application — an app bar, a stack of pages, a bar of destinations, a
sheet from the bottom, rows swiped aside, a snackbar — made of ordinary controls. It works
the same on Android, in a browser and in a desktop window: a desktop form can be a phone's
frame too, and the showcase's Mobile page is one, 360 × 640.

`dotnet new zf-app` makes an application built this way, for all three — see
[Getting started](Getting-Started#the-whole-application-from-a-template).

## The scaffold

```csharp
var inbox = new NavigationHost { RootPage = new InboxPage() };

Content = new Scaffold
{
    AppBar = new AppBar(),
    FloatingActionButton = composeButton,
    NavigationBar = new NavigationBar
    {
        Items =
        {
            new NavigationBarItem("Inbox", Icons.Inbox) { Content = inbox, Badge = "3" },
            new NavigationBarItem("Settings", Icons.Gear, () => new NavigationHost { RootPage = new SettingsPage() }),
        },
    },
};
```

`Scaffold` puts the app bar on top, the navigation bar at the bottom, the selected
destination's content between them and the floating button 16 px from the corner, above
the navigation bar. A destination's content is made the first time it is shown (a
`ContentFactory`, or the third argument) and kept afterwards, hidden while another is
shown: its stack, its scroll position and what was typed survive the switch. Tapping the
selected destination again takes its stack back to the root page.

Without a `Host` of its own, the app bar follows the stack of the destination shown.

## The stack of pages

`NavigationHost` is a stack of `Page`s, the current one on top:

| | |
| --- | --- |
| `RootPage`, `SetRoot(page)` | the first page; starts the stack anew |
| `PushAsync(page)` | a page over the current one — `new Page(content, title)` for ready content |
| `PopAsync()` | back to the previous page; false if there is none or the page stayed |
| `PopToRootAsync()` | back to the root in one step |
| `ReplaceAsync(page)` | in place of the current page — sign-in replaced by the account |
| `PushForResultAsync<T>(page)` | a page that hands back a result: `page.ReturnAsync(value)` |
| `Stack`, `CurrentPage`, `CanGoBack` | what is in it |
| `Navigating` (cancellable), `Navigated` | around every change |

A page hears `OnNavigatedTo` and `OnNavigatedFrom`, and may refuse to be left —
"the changes aren't saved":

```csharp
public class EditPage : Page
{
    protected override async Task<bool> CanLeaveAsync() =>
        !IsDirty || await MessageBox.ConfirmAsync(FindForm()!, "Discard the changes?");
}
```

The transition slides the new page in from the side (`Transition`,
`TransitionDurationMs`), mirrored for right-to-left; reduced motion turns it off. A swipe
from the left edge drags the current page away and goes back when let go far or fast
enough (`SwipeBackEnabled`, `SwipeBackEdgeWidth`).

### Routes

```csharp
host.RegisterRoute("message", parameters => new MessagePage(parameters["id"]));

await host.GoToAsync("message?id=42");     // pushes a MessagePage
await host.GoToAsync("/inbox/message?id=7"); // from the root: the root page's Route first
await host.GoToAsync("..");                // back one page
```

A location names pages by their routes, separated by `/`; the query goes to the last one
as `Page.Parameters`. A route that isn't registered throws `ArgumentException` before the
stack moves. `Location()` is the stack written the same way.

## Back

The system's back — Android's button or gesture, the browser's back button, Alt+Left, the
app bar's arrow — goes through `Form.RequestBack(source)`, which offers it in turn to:

1. an open flyout, menu or popup: it closes;
2. bottom sheets and navigation stacks, the innermost first — a stack goes back only while
   it is seen and has where to go;
3. `Form.BackRequested`, for the form's own say;
4. a dialog form, which is cancelled.

`RequestBack` returns whether anything took it. On Android `AndroidPlatform.BackRequested`
is raised only when nothing did — the place for "press back again to exit"; without a
handler the activity closes. Escape closes a bottom sheet the same way.

`HandlesBackRequests = false` keeps a stack out of it.

## In a browser: the address and the back button

A stack tracks the page's history (`TracksBrowserHistory`, true by default): each push
is an entry of the history, the stack's location after the `#`, and the browser's back
and forward buttons and the address follow the stack. A page opened at an address with a
route — a link to `…/#/inbox/message?id=7` — builds its stack by the routes, so links to
pages deep in the application work. The browser's back goes to the stack that pushed the
entry, even a destination of a scaffold that isn't shown.

Only a stack with routes writes its location into the address; one without leaves it to
one that has some.

## App bar

`AppBar` shows the title of the current page, a back arrow while its stack can go back
(`BackButtonMode`: `Auto`, `Always`, `Never`), the page's `Actions`, its own `Actions`,
and an overflow menu of `OverflowItems`. `NavigationIcon` replaces the arrow — a menu for a
drawer — and raises `NavigationClicked`.

```csharp
page.Actions.Add(new AppBarButton(Icons.Star, "Star", (_, _) => Star()));
```

## Navigation bar

`NavigationBar` is the bar of destinations at the bottom: `Items`, `SelectedIndex`,
`SelectionChanged`, `Reselected`. An item has a `Text`, an `Icon` and a `SelectedIcon`, and
a `Badge` — a number, or `""` for a dot. The arrow keys move between items; a screen reader
reads them as tabs.

## Bottom sheet

```csharp
var sheet = new BottomSheet(options);
await sheet.ShowAsync(form);   // ends when the sheet is closed
```

A sheet rises from the bottom over a scrim (`IsModal`); dragging it down, tapping the
scrim, back and Escape close it (`IsDismissible`). `Closing` may keep it open. At most
`MaxHeightRatio` of the form's height, at most `MaxSheetWidth` wide on a wide form.

## Swipe view

```csharp
var row = new SwipeView(content) { RightMode = SwipeMode.Execute };
row.LeftItems.Add(new SwipeItem("Star", Icons.Star, starColor));
row.RightItems.Add(new SwipeItem("Delete", Icons.Trash, red, (_, _) => Delete()));
```

A row swiped aside shows its items under it. `Reveal` leaves them open to be tapped;
`Execute` invokes the first one when swiped far enough. A right click, or the context menu
key, lists the items in a menu — on a desktop there may be nothing to swipe with.

## Snackbar

```csharp
if (await form.ShowSnackbarAsync("Message deleted", "Undo"))
    Restore();
```

A short message at the bottom with an optional action; true when the action was taken.
One at a time, queued; 4 seconds, 10 with an action. Above a scaffold's navigation bar and
the gesture bar. Styled as `Snackbar` — the theme draws it inverted.

## Safe area and the keyboard

`Form.SafeAreaInsets` is how far the system's bars and cutouts reach into the form:

- On Android, with `Form.EdgeToEdge = true`, the form draws under the status bar and the
  gesture bar and is told where they are. Without it the form is placed between them and
  the insets are zero.
- In a browser it is the page's `env(safe-area-inset-*)`: the page's viewport needs
  `viewport-fit=cover` (see [The browser](Browser#host-files)).

The app bar and the navigation bar color the space under the bars and keep their content
out of it; `SafeArea` does the same for any content (`Edges`).

When the on-screen keyboard makes the form shorter, the focused field is scrolled into
view in its scrolling panel. `UIElement.BringIntoView(margin)` does the same from code.
