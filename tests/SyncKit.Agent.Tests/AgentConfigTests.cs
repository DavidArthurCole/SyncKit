using SyncKit.Agent;

namespace SyncKit.Agent.Tests;

public class AgentConfigTests {
    [Fact]
    public void Parse_FullPipeline_DecodesStepsAndWatch() {
        const string yaml = """
        name: EggIncognito
        repo_url: https://github.com/DavidArthurCole/EggIncognito
        steps:
          - docker-pull: { ref: ghcr.io/x/y:latest, container: y }
          - portainer-update-stack
        watch:
          interval: 1m
          notify_bot_url: https://bot.example/internal/deploy-notify
        """;
        var cfg = AgentConfig.Parse(yaml);

        Assert.Equal("EggIncognito", cfg.Name);
        Assert.Equal(2, cfg.Steps.Count);
        Assert.IsType<DockerPull>(cfg.Steps[0]);
        Assert.IsType<PortainerUpdateService>(cfg.Steps[1]);
        Assert.Equal("ghcr.io/x/y:latest", ((DockerPull)cfg.Steps[0]).Ref);
        Assert.NotNull(cfg.Watch);
        Assert.Equal(TimeSpan.FromMinutes(1), cfg.Watch!.Interval);
        Assert.Equal("https://bot.example/internal/deploy-notify", cfg.Watch.NotifyBotUrl);
    }

    [Fact]
    public void Parse_PortainerStepAsMap_OverridesEnvNames() {
        const string yaml = """
        name: t
        steps:
          - portainer-update-stack: { stack_id_env: MY_STACK, endpoint_id_env: MY_EP }
        """;
        var cfg = AgentConfig.Parse(yaml);
        var step = Assert.IsType<PortainerUpdateService>(cfg.Steps[0]);
        Assert.Equal("MY_STACK", step.StackIdEnv);
        Assert.Equal("MY_EP", step.EndpointIdEnv);
        Assert.Equal("PORTAINER_API_URL", step.UrlEnv); // default kept
        Assert.False(step.PullImage); // default false (mixed local+registry stack safe)
    }

    [Fact]
    public void Parse_PortainerStep_PullImageTrue() {
        var cfg = AgentConfig.Parse("name: t\nsteps:\n  - portainer-update-stack: { pull_image: true }\n");
        Assert.True(((PortainerUpdateService)cfg.Steps[0]).PullImage);
    }

    [Fact]
    public void Parse_ShellStep_AlwaysTrue_SetsRunOnShortCircuit() {
        var cfg = AgentConfig.Parse("name: t\nsteps:\n  - shell: { run: echo hi, always: true }\n");
        var step = Assert.IsType<Shell>(cfg.Steps[0]);
        Assert.True(step.RunOnShortCircuit);
    }

    [Fact]
    public void Parse_ShellStep_AlwaysOmitted_DefaultsFalse() {
        var cfg = AgentConfig.Parse("name: t\nsteps:\n  - shell: { run: echo hi }\n");
        var step = Assert.IsType<Shell>(cfg.Steps[0]);
        Assert.False(step.RunOnShortCircuit);
    }

    [Fact]
    public void Parse_AppCallbackStep_DecodesUrlAndSecretEnv() {
        var cfg = AgentConfig.Parse("name: t\nsteps:\n  - app-callback: { url_env: EGI_RUNNER_DEPLOY_URL, secret_env: EGI_RUNNER_DEPLOY_SECRET }\n");
        var step = Assert.IsType<AppCallback>(cfg.Steps[0]);
        Assert.Equal("EGI_RUNNER_DEPLOY_URL", step.UrlEnv);
        Assert.Equal("EGI_RUNNER_DEPLOY_SECRET", step.SecretEnv);
    }

    [Fact]
    public void Parse_UnknownStep_Throws() =>
        Assert.Throws<FormatException>(() => AgentConfig.Parse("name: t\nsteps:\n  - bogus-step\n"));

    [Theory]
    [InlineData("1m", 60)]
    [InlineData("5m0s", 300)]
    [InlineData("30s", 30)]
    [InlineData("1h", 3600)]
    public void ParseDuration_Units(string s, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), AgentConfig.ParseDuration(s));
}
