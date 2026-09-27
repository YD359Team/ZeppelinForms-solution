using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ZeppelinForms.Windows")]
[assembly: InternalsVisibleTo("ZeppelinForms.Linux")]
[assembly: InternalsVisibleTo("ZeppelinForms.Browser")]
[assembly: InternalsVisibleTo("ZeppelinForms.Android")]
[assembly: InternalsVisibleTo("ZeppelinForms.Skia")]
// for tests — to check internal mechanisms where they live: stepping
// the frame clock on command instead of a stopwatch, the state of transitions
[assembly: InternalsVisibleTo("ZeppelinForms.UnitTests")]