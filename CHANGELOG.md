# Changes

## Feature [0.7.0]

### Features
- Add Ripple effect for buttons
- Add Color.Lerp(Color,Color,float)
- Add new colors in Colors class
- Add constructor with single UIElement for WrapPanel
- StackPanel now can align children
- Add MainAxisAlignment
- Add CrossAxisAlignment

### New controls
- Add PageControl 
- Add Page
- Add PageIndicator

## [0.6.0] — 2026-09-03

### Breaking changes
- Old class LightThemeColors removed

### Features
- Add Display API
- Add OnPreviewMouseDown
- Right\Middle mouse button events
- Mouse events without location	now has location
- Now disabled control have special filling
- Add FlexGrow support for StackPanel
- Add RowSpan and ColumnSpan for UniformGrid

### New controls
- Add MapControl
- Add GroupBox

### Fixes
- Fix all flyout controls. Now all flyouts will be closed correct
- Fix click in clickable UIElement in ListBox dont raise selection
- Fix pressed buttons blink
- Fix ComboBox flyout height
- Fix Calendar text lag

## [0.5.0] — 2026-09-02

### Breaking changes
- Rectangle.AsSize() -> Rectangle.Size
- Rectangle.AsPosition() -> Rectangle.Position
- Core.Text classes moved to *.Controls.Text

### Features
- Add MessageBox
- Add InputBox
- Add Form.IsDialog
- Add theme support
- Extracts ButtonBase class 
- Now Primary, Secondary и Danger button is new classes with custom themes
- Add validation to TextBox
- Add watermark to TextBox

### Fixes
- ToggleSwitch now changes color after checking state changed

### New controls
- Add SplitContainer
- Add GridSplitter
- Add MaskedTextBox
- Add HintLabel

## [0.4.0] — 2026-09-02

### Features
- Add headless platform
- Add RTL support
- Add ItemsPanel incremental update
- Rich text support
- Cursor support

### New controls

- Add VirtualizedStackPanel
- Add RichLabel and LinkLabel
- Add Shape and RectangleShape, EllipseShape, LineShape, PolygonShape

## [0.3.0] — 2026-08-30

### Added

- Add Grid.LengthAuto
- Add SplitButton
- Add ToggleButton
- Add SplitButton
- Add PieChart
- Add BarChart
- Add LineChart
- Add Graphics.FillPie
- Add PanelControl

### Fixes and extensions

- Now all panels can be scrollable
- ScrollViewer now is lightweight panel with default overflow settings
- Add ProgressBar display text customization
- NumericUpDown  contains keyboard input, dot, caret
- Fix mouse hover effects
- Change example projects

## [0.2.1] — 2026-08-30

- Hot fix Linux tests & CI

## [0.2.0] — 2026-08-30

### Add

- Linux support (X11)
- More controls added
- Form debugger
- Clipboard
- Animation, rotation support
- TextBox emoji support

### Fixes

- More fixes