using Xunit;

namespace DigitalBrain.Simulation.Tests;

// These scenarios publish several projects from one SDK checkout. Keep unrelated
// test silos/builds out of that resource budget; concurrency inside each scenario stays real.
[CollectionDefinition("Application graph builds", DisableParallelization = true)]
public sealed class ApplicationGraphBuildCollection;
