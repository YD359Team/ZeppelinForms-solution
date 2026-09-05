# ZeppelinForms

![Logo](ZF_medium.png)

## ENG

**ZeppelinForms** (ZF) is an experimental project aimed at creating a simple, platform-independent UI framework with hardware acceleration (on Windows) and straightforward code-behind UI development.

### ⚙️ Current Status

![CI](https://github.com/YD359Team/ZeppelinForms-solution/actions/workflows/ci.yml/badge.svg)

The project is under active development.

| # | Name        | Status |
| - | ----------- | ------ |
| 1 | Headless    | ✅      |
| 2 | Windows     | ✅      |
| 3 | Linux (X11) | ✅      |
| 4 | WebAssembly | 💡     |
| 5 | macOS       | 💡     |

### 🧠 Philosophy

In short: combine the simplicity of WinForms with selected ideas from WPF and Flutter, the cross-platform capabilities of Avalonia, and get rid of tons of legacy baggage along the way.

* No dependency on a specific platform
* No dependency on a specific graphics stack

And if the project is modern, why not take advantage of the full capabilities of .NET 10 and C# 14?

### 🖌️ Rendering

| # | Name      | Status |
| - | --------- | ------ |
| 1 | SkiaSharp | ✅      |
| 2 | DirectX   | 💡     |

ZeppelinForms itself knows nothing about Skia, because it is implemented in a separate project, `ZeppelinForms.Skia`. This means that the graphics layer is completely decoupled from the framework logic.

As a result, the framework can be integrated with DirectX or virtually any other graphics stack.

If hardware acceleration is unavailable, the framework falls back to software rendering.

### 📟 Forms

**Forms** are the only type of window in ZeppelinForms, just like in WinForms.

| # | Name                  | Status |
| - | --------------------- | ------ |
| 1 | Debugger              | ✅*     |
| 2 | Overlays              | ✅**    |
| 3 | Toast Notifications   | ✅***   |
| 4 | ToolTips              | ✅      |
| 5 | Dialog Windows        | ✅      |
| 6 | Open/Save File Dialog | 💡     |
| 7 | MessageBox            | ✅      |
| 8 | InputBox              | ✅      |
| 9 | Clipboard             | ✅      |

* — the inspector currently works only with certain types
** — the API will be extended further
*** — some limitations and unfinished parts remain

### 🛣️ Layout

* Supports `Measure` and `Arrange`
* All controls support docking
* Supports horizontal and vertical alignment, including content alignment

### 🧩 Controls

All controls must inherit from `UIElement`, either directly or indirectly through `UnitControl`, `PanelControl`, or `WrapControl`.

There are no WinForms-style components that are not considered actual controls, such as `Timer` or `BackgroundWorker`.

#### UIElement

The common base type for all controls. A form is not a `UIElement`, but its `Content` can be any `UIElement`.

Unlike WinForms, all controls support:

* Internal padding
* Transparency
* Shadows (`box-shadow`)
* Scaling

#### Unit Controls

`UIElement` → `UnitControl`

**Unit controls** are similar to `Control` in WinForms and can be thought of as regular controls. They cannot contain child controls.

|  № | Название            | Статус |  № | Название        | Статус |
| -: | ------------------- | :----: | -: | --------------- | :----: |
|  1 | Label               |    ✅   | 21 | ToggleButton    |    ✅   |
|  2 | Button              |    ✅   | 22 | BarChart        |    ✅   |
|  3 | CheckBox            |    ✅   | 23 | LineChart       |    ✅   |
|  4 | PictureBox          |    ✅   | 24 | PieChart        |    ✅   |
|  5 | RadioButton         |    ✅   | 25 | RichLabel       |    ✅   |
|  6 | TextBox             |   ✅*   | 26 | LinkLabel       |    ✅   |
|  7 | ToggleSwitch        |    ✅   | 27 | ShapeLine       |    ✅   |
|  8 | DateTimePicker      |    ✅   | 28 | ShapeRectangle  |    ✅   |
|  9 | TimePicker          |    ✅   | 29 | ShapeEllipse    |    ✅   |
| 10 | ColorPicker         |    ✅   | 30 | ShapePolygon    |    ✅   |
| 11 | ScrollBar           |    ✅   | 31 | CheckedComboBox |    ✅   |
| 12 | SvgIcon             |    ✅   | 32 | ComboBox        |    ✅   |
| 13 | NumericUpDown       |    ✅   | 33 | GridSplitter    |    ✅   |
| 14 | ProgressBar         |    ✅   | 34 | MaskedTextBox   |    ✅   |
| 15 | CircularProgressBar |    ✅   | 35 | HintLabel       |    ✅   |
| 16 | TrackBar            |    ✅   | 36 | MapControl      |    ✅   |
| 17 | Calendar            |    ✅   | 37 | PropertyGrid    |   ✅**  |
| 18 | MenuBar             |    ✅   |    |                 |        |
| 19 | MenuList            |    ✅   |    |                 |        |
| 20 | SplitButton         |    ✅   |    |                 |        |


* — contains bugs and is missing part of its API
** — currently works only with certain controls

#### Panels

`UIElement` → `PanelControl`

**Panels** are controls that can contain other controls, including other panels.

| # | Name                   | Status |
| - | ---------------------- | ------ |
| 1 | Panel                  | ✅      |
| 2 | StackPanel             | ✅      |
| 3 | Grid                   | ✅      |
| 4 | DockPanel              | ✅      |
| 5 | TabControl             | ✅      |
| 6 | UniformGrid            | ✅      |
| 7 | VirtualizingStackPanel | ✅      |
| 8 | SplitContainer         | ✅      |

##### Items Panels

`UIElement` → `PanelControl` → `ItemsControl`

A specialized type of panel capable of working with collections of elements.

| # | Name           | Status |
| - | -------------- | ------ |
| 1 | ListBox        | ✅      |
| 2 | CheckedListBox | ✅      |
| 3 | ScrollViewer   | ✅      |
| 4 | TreeView       | 💡     |
| 5 | DataGrid       | 💡     |

⭐ All panels can display a scrollbar when their content overflows.

#### Wrapper Controls

`UIElement` → `WrapControl`

**Wrapper controls** are controls that can contain a single child control. This concept is unusual in the WinForms world, but familiar from XAML-based frameworks.

| # | Name          | Status |
| - | ------------- | ------ |
| 1 | Border        | ✅      |
| 2 | Spoiler       | ✅      |
| 3 | ZoomBox       | ✅      |
| 4 | GroupBox      | ✅      |
| 5 | LayoutBuilder | ✅      |

### 🎄 Themes

The framework supports themes and currently includes built-in light and dark themes.

### 🛠️ Code Examples

Creating an application on Windows:

```csharp
public class Program
{
    static void Main()
    {
        WindowsPlatform windowsPlatform = new();
        App myApp = new(windowsPlatform)
        {
            MainForm = new MainForm()
        };
        myApp.Run();
    }
}
```

Creating an application on Linux (X11):

```csharp
public class Program
{
    static void Main()
    {
        X11Platform linuxPlatform = new();
        App myApp = new(linuxPlatform)
        {
            MainForm = new MainForm()
        };
        myApp.Run();
    }
}
```

See the projects in the `examples/` directory for more examples.

### 🧪 Snapshot Tests

Reference snapshots are stored in:

`tests/ZeppelinForms.UnitTests/Snapshots/Expected/{win,linux}`

Text rendering differs between platforms, so separate snapshot sets are maintained for Windows and Linux.

After making an intentional visual change:

1. **Locally:** run
   `dotnet test --settings tests/ZeppelinForms.UnitTests/updatesnapshots.runsettings`
2. Review the PNG changes using `git diff` and commit the updated snapshots.
3. **For Linux:** temporarily add `ZF_UPDATE_SNAPSHOTS: 'true'` to the test step in `ci.yml`, run the tests, retrieve the updated snapshots from the CI artifacts, commit them, and remove the environment variable.


## RUS

**ZeppelinForms** (ZF) - проект-эксперимент по созданию простого UI-фреймворка без привязки к Windows, с аппаратным ускорением (для Windows), простым code-behind созданием элементов. 

### ⚙️ Текущий статус

![CI](https://github.com/YD359Team/ZeppelinForms-solution/actions/workflows/ci.yml/badge.svg)

Проект находится в активной разработке.

| № | Название | Статус |
|---|------------|---|
| 1 | Headless | ✅ |
| 2 | Windows | ✅ |
| 3 | Linux (X11) | ✅ |
| 4 | WebAssembly | 💡 |
| 5 | MacOS | 💡 |

### 🧠 Философия

Если коротко: объединить простоту WinForms с некоторыми идеями WPF и Flutter, кроссплатформенностью Avalonia, и выкинув тонны легаси в процессе.

- Не привязываться к конкретной платформе
- Не привязываться к конкретному графическому стеку

А если проект современный, то почему бы не использовать все богатые возможности .NET 10 и C# 14?

### 🖌️ Рендеринг

| № | Название | Статус |
|---|------------|---|
| 1 | SkiaSharp | ✅ |
| 2 | DirectX | 💡 |

ZeppelinForms ничего не знает про Skia, потому что он существует в отдельном проекте ZeppelinForms.Skia, то есть графика полностью абстрагирована от логики. \
Из этого следует, что фреймворк можно встроить в DirectX или вообще любой графический стек! \
Если аппаратное ускорение недоступно, то идет откат на программный рендер.

### 📟 Формы

**Формы** - это единственный вид окон, как и в WinForms.

| № | Название | Статус |
|---|------------|---|
| 1 | Отладчик | ✅* |
| 2 | Наложения | ✅** |
| 3 | Всплывающие сообщения | ✅*** |
| 4 | ToolTips | ✅ |
| 5 | Диалоговые окна | ✅ |
| 6 | Open\SaveFileDialog | 💡 |
| 7 | MessageBox | ✅ |
| 8 | InputBox | ✅ |
| 9 | Буфер обмена | ✅ |

\* - инспектор работает только для некоторых типов \
\** - API еще будет дополняться \
\*** - есть недоработки

### 🛣️ Компоновка

- Поддержка Measure и Arrange
- Все контролы имеют Docking
- Горизонтальное и вертикальное выравнивание (в том числе контента)

### 🧩 Контролы

Все контролы должны являться наследниками `UIElement` явно или неявно - наследуя `UnitControl`, `PanelControl` или `WrapControl`. \
Никаких компонентов из WinForms, не являющихся контролами в полном смысле (например, Timer или BackgroundWorker), не предусмотрено.

#### UIElement

Общий предок для всех контролов. Форма не является UIElement, но ее `Content` может быть любым UIElement. \
В отличии от WinForms, все контролы поддерживают внутренние отступы, прозрачность, тень (boxshadow), могут масштабироваться.

#### Единичные контролы

`UIElement` -> `UnitControl`

**Единичные контролы** - похожи на Control из мира WinForms, можно назвать их обычными контролами. У них не может быть дочерних контролов.

| № | Название      | Статус |
|---|------------|---|
| 1 | Label      | ✅ |
| 2 | Button     | ✅ |
| 3 | CheckBox   | ✅ |
| 4 | PictureBox | ✅ |
| 5 | RadioButton | ✅ |
| 6 | TextBox | ✅* |
| 7 | ToggleSwitch | ✅ |
| 8 | DateTimePicker | ✅ |
| 9 | TimePicker | ✅ |
| 10 | ColorPicker | ✅ |
| 11 | ScrollBar | ✅ |
| 12 | SvgIcon | ✅ |
| 13 | NumericUpDown | ✅ |
| 14 | ProgressBar | ✅ |
| 15 | CircularProgressBar | ✅ |
| 16 | TrackBar | ✅ |
| 17 | Calendar | ✅ |
| 18 | MenuBar | ✅ |
| 19 | MenuList | ✅ |
| 20 | SplitButton | ✅ |
| 21 | ToggleButton | ✅ |
| 22 | BarChart | ✅ |
| 23 | LineChart | ✅ |
| 24 | PieChart | ✅ |
| 25 | RichLabel | ✅ |
| 26 | LinkLabel | ✅ |
| 27 | ShapeLine | ✅ |
| 28 | ShapeRectangle | ✅ |
| 29 | ShapeEllipse | ✅ |
| 30 | ShapePolygon | ✅ |
| 31 | CheckedComboBox | ✅ |
| 32 | ComboBox | ✅ |
| 33 | GridSplitter | ✅ |
| 34 | MaskedTextBox | ✅ |
| 35 | HintLabel | ✅ |
| 36 | MapControl | ✅ |
| 37 | PropertyGrid | ✅** |

\* - есть баги и отсутствует часть API \
\** - пока работает только для некоторых контролов

#### Панели

`UIElement` -> `PanelControl`

**Панели** - контролы, которые могут включать в себя другие контролы (в том числе другие панели).

| № | Название      | Статус |
|---|------------|---|
| 1 | Panel | ✅ |
| 2 | StackPanel | ✅ |
| 3 | Grid | ✅ |
| 4 | DockPanel | ✅ |
| 5 | TabControl | ✅ |
| 6 | UniformGrid | ✅ |
| 7 | VirtualizingStackPanel | ✅ |
| 8 | SplitContainer | ✅ |

##### Панели элементов

`UIElement` -> `PanelControl` -> `ItemsControl`

Подвид панели, способный работать с коллекциями элементов.

| № | Название | Статус |
|---|------------|---|
| 1 | ListBox | ✅ |
| 2 | CheckedListBox | ✅ |
| 3 | ScrollViewer | ✅ |
| 4 | TreeView | 💡 |
| 5 | DataGrid | 💡 |

⭐ Все панели могут иметь скроллбар при переполнении.

#### Контролы-обёртки

`UIElement` -> `WrapControl`

**Контролы-обёртки** - контролы, которые могут включать в себя один контрол. Необычная для WinForms мира идея, но знакомая в мире XAML-фреймворков.

| № | Название      | Статус |
|---|------------|---|
| 1 | Border      | ✅ |
| 2 | Spoiler     | ✅ |
| 3 | ZoomBox     | ✅ |
| 4 | GroupBox     | ✅ |
| 5 | LayoutBuilder | ✅ |

### 🎄 Темы

Имеется поддержка тем, есть предустановленные светлая и тёмная темы.

### 🛠️ Примеры кода

Создание приложения в Windows

```csharp
public class Program
{
    static void Main()
    {
        WindowsPlatform windowsPlatform = new();
        App myApp = new(windowsPlatform)
        {
            MainForm = new MainForm()
        };
        myApp.Run();
    }
}
```

Создание приложения в Linux (X11)

```csharp
public class Program
{
    static void Main()
    {
        X11Platform linuxPlatform = new();
        App myApp = new(linuxPlatform)
        {
            MainForm = new MainForm()
        };
        myApp.Run();
    }
}
```

Смотрите проекты в папке `examples/`, чтобы узнать больше.

### 🧪 Снимковые тесты

Эталоны хранятся в `tests/ZeppelinForms.UnitTests/Snapshots/Expected/{win,linux}` —
отрисовка текста между платформами различается, поэтому наборы отдельные.

После осознанного изменения внешнего вида:

1. Локально: `dotnet test --settings tests/ZeppelinForms.UnitTests/updatesnapshots.runsettings`
2. Проверить `git diff` по PNG, закоммитить.
3. Для Linux: временно добавить `ZF_UPDATE_SNAPSHOTS: 'true'` в шаг тестов `ci.yml`,
   прогнать, забрать эталоны артефактом, закоммитить, убрать переменную.
