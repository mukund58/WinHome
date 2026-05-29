using System;
using System.IO;
using Moq;
using WinHome.Infrastructure;
using WinHome.Interfaces;
using WinHome.Models;
using Xunit;

namespace WinHome.Tests;

public class AppRunnerTests
{
  [Fact]
  public async Task RunAsync_ConfigMissing_ReturnsOne()
  {
    var logger = new Mock<ILogger>();
    var validator = new Mock<IConfigValidator>();
    var secretResolver = new Mock<ISecretResolver>();
    var engine = new Mock<IEngine>();

    var runner = new AppRunner(engine.Object, validator.Object, secretResolver.Object, logger.Object);
    var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.yaml");

    var exitCode = await runner.RunAsync(new FileInfo(missingPath), dryRun: false, profile: null, debug: false, diff: false, json: false);

    Assert.Equal(1, exitCode);
    logger.Verify(l => l.LogError(It.Is<string>(msg => msg.Contains("Configuration file not found"))), Times.Once);
    validator.Verify(v => v.Validate(It.IsAny<string>()), Times.Never);
    secretResolver.Verify(r => r.ResolveObject(It.IsAny<object>()), Times.Never);
    engine.Verify(e => e.RunAsync(It.IsAny<Configuration>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
  }

  [Fact]
  public async Task RunAsync_ValidationFails_ReturnsOneAndLogsErrors()
  {
    var logger = new Mock<ILogger>();
    var validator = new Mock<IConfigValidator>();
    var secretResolver = new Mock<ISecretResolver>();
    var engine = new Mock<IEngine>();

    var configFile = CreateTempConfigFile("version: \"1.0\"\n");
    try
    {
      validator.Setup(v => v.Validate(It.IsAny<string>()))
          .Returns((false, new List<string> { "bad-config", "missing-field" }));

      var runner = new AppRunner(engine.Object, validator.Object, secretResolver.Object, logger.Object);

      var exitCode = await runner.RunAsync(configFile, dryRun: false, profile: null, debug: false, diff: false, json: false);

      Assert.Equal(1, exitCode);
      validator.Verify(v => v.Validate(It.Is<string>(text => text.Contains("version"))), Times.Once);
      logger.Verify(l => l.LogError(It.Is<string>(msg => msg.Contains("Configuration validation failed"))), Times.Once);
      logger.Verify(l => l.LogError(It.Is<string>(msg => msg.Contains("bad-config"))), Times.Once);
      logger.Verify(l => l.LogError(It.Is<string>(msg => msg.Contains("missing-field"))), Times.Once);
      secretResolver.Verify(r => r.ResolveObject(It.IsAny<object>()), Times.Never);
      engine.Verify(e => e.RunAsync(It.IsAny<Configuration>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }
    finally
    {
      if (configFile.Exists)
      {
        configFile.Delete();
      }
    }
  }

  [Fact]
  public async Task RunAsync_ValidConfig_ResolvesSecretsAndRunsEngine()
  {
    var logger = new Mock<ILogger>();
    var validator = new Mock<IConfigValidator>();
    var secretResolver = new Mock<ISecretResolver>();
    var engine = new Mock<IEngine>();

    var yaml = """
version: "1.0"
profiles:
  work: {}
""";
    var configFile = CreateTempConfigFile(yaml);
    try
    {
      validator.Setup(v => v.Validate(It.IsAny<string>()))
          .Returns((true, new List<string>()));

      Configuration? capturedConfig = null;
      secretResolver
          .Setup(r => r.ResolveObject(It.IsAny<object>()))
          .Callback<object>(obj => capturedConfig = obj as Configuration);

      engine.Setup(e => e.RunAsync(It.IsAny<Configuration>(), true, "work", false, false, false, false))
          .Returns(Task.CompletedTask);

      var runner = new AppRunner(engine.Object, validator.Object, secretResolver.Object, logger.Object);

      var exitCode = await runner.RunAsync(configFile, dryRun: true, profile: "work", debug: false, diff: false, json: false);

      Assert.Equal(0, exitCode);
      Assert.NotNull(capturedConfig);
      Assert.True(capturedConfig!.Profiles.ContainsKey("work"));
      secretResolver.Verify(r => r.ResolveObject(It.IsAny<object>()), Times.Once);
      engine.Verify(e => e.RunAsync(It.Is<Configuration>(cfg => cfg.Version == "1.0"), true, "work", false, false, false, false), Times.Once);
    }
    finally
    {
      if (configFile.Exists)
      {
        configFile.Delete();
      }
    }
  }

  [Fact]
  public async Task RunAsync_WhenValidatorThrows_LogsFatalAndReturnsOne()
  {
    var logger = new Mock<ILogger>();
    var validator = new Mock<IConfigValidator>();
    var secretResolver = new Mock<ISecretResolver>();
    var engine = new Mock<IEngine>();

    var configFile = CreateTempConfigFile("version: \"1.0\"\n");
    try
    {
      validator.Setup(v => v.Validate(It.IsAny<string>()))
          .Throws(new InvalidOperationException("boom"));

      var runner = new AppRunner(engine.Object, validator.Object, secretResolver.Object, logger.Object);

      var exitCode = await runner.RunAsync(configFile, dryRun: false, profile: null, debug: true, diff: false, json: false);

      Assert.Equal(1, exitCode);
      logger.Verify(l => l.LogError(It.Is<string>(msg => msg.Contains("[Fatal]") && msg.Contains("boom"))), Times.Once);
      logger.Verify(l => l.LogError(It.IsAny<string>()), Times.Exactly(2));
      engine.Verify(e => e.RunAsync(It.IsAny<Configuration>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }
    finally
    {
      if (configFile.Exists)
      {
        configFile.Delete();
      }
    }
  }

  private static FileInfo CreateTempConfigFile(string content)
  {
    var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.yaml");
    File.WriteAllText(path, content);
    return new FileInfo(path);
  }
}
