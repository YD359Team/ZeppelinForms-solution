using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Drawing;

public interface ITextMeasurer
{
    Size MeasureText(string text, Font font);

   /// <summary>Ширина первых length символов. Нужна для позиционирования
   /// каретки: складывать ширины по одному символу нельзя из-за кернинга.</summary>
   float MeasureTextWidth(string text, int length, Font font);

   Size MeasureRuns(IReadOnlyList<TextRun> runs, Font baseFont);


    /// <summary>Готов ли шрифт к измерению. Там, где шрифты грузятся
    /// асинхронно, до готовности возвращаются метрики запасного.</summary>
    bool IsReady(Font font);

    /// <summary>Загрузить шрифт. На настольных платформах завершается сразу.</summary>
    Task PrepareAsync(Font font);
}