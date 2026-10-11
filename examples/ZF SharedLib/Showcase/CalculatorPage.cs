using System.Globalization;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>A small application on one page: a calculator laid out with a grid.</summary>
internal static class CalculatorPage
{
    public static UIElement Create() =>
        Demo.Page("Calculator", "A small application built from a Grid, buttons and a text box.",
            Demo.Section("Calculator", Calculator(), "Four Star columns; the display and the = key span all of them"));

    private static Grid Calculator()
    {
        var grid = new Grid
        {
            Columns = "*,*,*,*",
            Rows = "Auto,*,*,*,*,*",
            Size = new(340, 400),
            Font = Font.Default.WithSize(18f),
        };

        var display = new TextBox
        {
            Text = "0",
            IsReadOnly = true,
            HorizontalContentAlign = HorizontalContentAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new(3),
            ColumnSpan = 4,
        };

        // the state lives in the closures: each calculator has its own
        double accumulator = 0;
        char pending = '\0';
        bool startNewNumber = true;

        double Current() =>
            double.TryParse(display.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : 0;

        void Show(double value) =>
            display.Text = value.ToString("0.##########", CultureInfo.InvariantCulture);

        void Reset()
        {
            accumulator = 0;
            pending = '\0';
            startNewNumber = true;
            display.Text = "0";
        }

        void AppendDigit(string digit)
        {
            if (startNewNumber)
            {
                display.Text = digit;
                startNewNumber = false;
                return;
            }

            // a lone zero is replaced, not appended to
            display.Text = display.Text == "0" ? digit : display.Text + digit;
        }

        void AppendDot()
        {
            if (startNewNumber)
            {
                display.Text = "0.";
                startNewNumber = false;
                return;
            }

            if (!display.Text!.Contains('.'))
                display.Text += ".";
        }

        // false when the operation is impossible: the display already shows the error
        bool ApplyPending()
        {
            if (pending == '\0')
            {
                accumulator = Current();
                return true;
            }

            double right = Current();

            if (pending == '/' && right == 0)
            {
                display.Text = "Divide by zero";
                pending = '\0';
                accumulator = 0;
                startNewNumber = true;
                return false;
            }

            accumulator = pending switch
            {
                '+' => accumulator + right,
                '-' => accumulator - right,
                '*' => accumulator * right,
                '/' => accumulator / right,
                _ => right,
            };

            Show(accumulator);
            return true;
        }

        void SetOperator(char op)
        {
            // operators pressed one after another must not pile up calculations:
            // while no number has been typed, only the operator changes
            if (!startNewNumber && !ApplyPending())
                return;

            if (startNewNumber && pending == '\0')
                accumulator = Current();

            pending = op;
            startNewNumber = true;
        }

        void Equals()
        {
            if (!ApplyPending()) return;

            pending = '\0';
            startNewNumber = true;
        }

        Button Key(string text, int row, int column, Action action, int columnSpan = 1, Button? kind = null)
        {
            Button button = kind ?? new Button();
            button.Text = text;
            button.Row = row;
            button.Column = column;
            button.ColumnSpan = columnSpan;
            button.Margin = new(3);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.VerticalAlignment = VerticalAlignment.Stretch;

            button.Click += (_, _) => action();

            return button;
        }

        Button Digit(string text, int row, int column) =>
            Key(text, row, column, () => AppendDigit(text));

        // the key shows the typographic sign; the calculation uses the plain one
        Button Operator(string text, char op, int row, int column) =>
            Key(text, row, column, () => SetOperator(op), kind: new SecondaryButton());

        grid.Children.AddRange([
            display,

            Digit("7", 1, 0), Digit("8", 1, 1), Digit("9", 1, 2), Operator("+", '+', 1, 3),
            Digit("4", 2, 0), Digit("5", 2, 1), Digit("6", 2, 2), Operator("−", '-', 2, 3),
            Digit("1", 3, 0), Digit("2", 3, 1), Digit("3", 3, 2), Operator("×", '*', 3, 3),

            Key("C", 4, 0, Reset, kind: new DangerButton()),
            Digit("0", 4, 1),
            Key(".", 4, 2, AppendDot),
            Operator("÷", '/', 4, 3),

            Key("=", 5, 0, Equals, columnSpan: 4, kind: new PrimaryButton()),
        ]);

        return grid;
    }
}
