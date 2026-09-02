# ZeppelinForms

![Logo](ZF_medium.png)

## RUS

**ZeppelinForms** (ZF) - проект-эксперимент по созданию простого UI-фреймворка без привязки к Windows, с аппаратным ускорением (для Windows), простым code-behind созданием элементов. 

### Текущий статус

![CI](https://github.com/YD359Team/ZeppelinForms-solution/actions/workflows/ci.yml/badge.svg)

Проект находится в активной разработке, API пока нестабильный.

| № | Название | Статус |
|---|------------|---|
| 1 | Windows | ✅ |
| 2 | Linux (X11) | ✅ |
| 3 | MacOS | 💡 |

### Философия

Объединить простоту WinForms с некоторыми идеями WPF, кроссплатформенностью Avalonia, и выкинув тонны легаси в процессе. \
А если проект современный, то почему бы не использовать все богатые возможности .NET 10 и C# 14? 

### Рендеринг

ZeppelinForms ничего не знает про Skia, потому что он существует в отдельном проекте ZeppelinForms.Skia, то есть графика полностью абстрагирована от логики. \
Из этого следует, что фреймворк можно встроить в DirectX или вообще любой графический стек! \
Если аппаратное ускорение недоступно, то идет откат на программный рендер.

### Формы

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

### Контролы

Все контролы должны являться наследниками `UIElement` явно или неявно - наследуя `UnitControl`, `PanelControl` или `WrapControl`. \
Никаких компонентов из WinForms, не являющихся контролами в полном смысле (например, Timer или BackgroundWorker), не предусмотрено.

#### UIElement

Общий предок для всех контролов. Форма не является UIElement, но ее `Content` может быть любым UIElement. \
В отличии от WinForms, все контролы поддерживают внутренние отступы, прозрачность, тень (boxshadow), могут масштабироваться.

#### Единичные контролы

`UIElement` -> `UnitControl`

**Единичные контролы** - похожи на Control из мира WinForms, можно назвать их обычными контолами. У них не может быть дочерних контролов.

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
| 29 | ShapeEllipse) | ✅ |
| 30 | ShapePolygon) | ✅ |
| 31 | CheckedComboBox | ✅ |
| 32 | ComboBox | ✅ |
| 33 | GridSplitter | ✅ |
| 34 | MaskedTextBox | ✅ |
| 35 | HintLabel | ✅ |

\* - есть баги и отсуствует часть API

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
| 7 | VirtualizedStackPanel | ✅ |
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

### Примеры кода

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

### Снимковые тесты

Эталоны хранятся в `tests/ZeppelinForms.UnitTests/Snapshots/Expected/{win,linux}` —
отрисовка текста между платформами различается, поэтому наборы отдельные.

После осознанного изменения внешнего вида:

1. Локально: `dotnet test --settings tests/ZeppelinForms.UnitTests/updatesnapshots.runsettings`
2. Проверить `git diff` по PNG, закоммитить.
3. Для Linux: временно добавить `ZF_UPDATE_SNAPSHOTS: 'true'` в шаг тестов `ci.yml`,
   прогнать, забрать эталоны артефактом, закоммитить, убрать переменную.