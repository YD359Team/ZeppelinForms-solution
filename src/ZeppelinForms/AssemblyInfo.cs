using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ZeppelinForms.Windows")]
[assembly: InternalsVisibleTo("ZeppelinForms.Linux")]
[assembly: InternalsVisibleTo("ZeppelinForms.Browser")]
[assembly: InternalsVisibleTo("ZeppelinForms.Android")]
[assembly: InternalsVisibleTo("ZeppelinForms.Skia")]
// тестам — чтобы проверять внутренние механизмы там, где они и живут:
// шаг часов кадра по команде вместо секундомера, состояние переходов
[assembly: InternalsVisibleTo("ZeppelinForms.UnitTests")]
