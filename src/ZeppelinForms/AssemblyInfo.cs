using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ZeppelinForms.Windows")]
[assembly: InternalsVisibleTo("ZeppelinForms.Linux")]
[assembly: InternalsVisibleTo("ZeppelinForms.Browser")]
[assembly: InternalsVisibleTo("ZeppelinForms.Android")]
[assembly: InternalsVisibleTo("ZeppelinForms.Skia")]
[assembly: InternalsVisibleTo("ZeppelinForms.Gles")]
[assembly: InternalsVisibleTo("ZeppelinForms.Design")]
// for tests — to check internal mechanisms where they live: stepping
// the frame clock on command instead of a stopwatch, the state of transitions
[assembly: InternalsVisibleTo("ZeppelinForms.UnitTests")]
// for the benchmarks — to step the frame clock as the tests do: a frame of many
// animations is measured without a window delivering frames
[assembly: InternalsVisibleTo("ZeppelinForms.Benchmarks")]