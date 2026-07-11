using SyncKit.Config;

namespace SyncKit.Config.Tests;

public class SharedConfigTests {
    [Fact]
    public void Load_FileAbsent_FallsBackToEnvEntirely() {
        var cfg = SharedConfig.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".yaml"),
            key => key == "SHARED_ROLE_ID" ? "env-role" : null);

        Assert.Equal("env-role", cfg.SharedRoleId);
        Assert.Null(cfg.DeployAgentUrl);
    }

    [Fact]
    public void Load_FilePresent_UsesFileValues() {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".yaml");
        File.WriteAllText(path, "sharedRoleId: \"file-role\"\ndeployAgentUrl: \"http://file/deploy\"\n");
        try {
            var cfg = SharedConfig.Load(path, _ => "env-fallback-should-not-be-used");

            Assert.Equal("file-role", cfg.SharedRoleId);
            Assert.Equal("http://file/deploy", cfg.DeployAgentUrl);
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_PartialFile_FallsBackPerMissingKey() {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".yaml");
        File.WriteAllText(path, "sharedRoleId: \"file-role\"\n");
        try {
            var cfg = SharedConfig.Load(path, key => key == "DEPLOY_AGENT_URL" ? "env-deploy-url" : null);

            Assert.Equal("file-role", cfg.SharedRoleId);
            Assert.Equal("env-deploy-url", cfg.DeployAgentUrl);
        } finally { File.Delete(path); }
    }
}
