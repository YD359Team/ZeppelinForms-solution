using Xunit;

namespace ZeppelinForms.ExternalControlTests;

/// <summary>The tests switch App.Theme and Font.Default, which are global, and every
/// open form follows a switch: a test running beside another restyles its controls
/// under it. Run them one at a time, as ZeppelinForms.UnitTests runs its own.</summary>
[CollectionDefinition("Platform", DisableParallelization = true)]
public class PlatformCollection;