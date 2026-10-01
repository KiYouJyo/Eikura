using System.Text.Json;
using System.Text.Json.Nodes;
using Eizo;

public sealed class ConfigBackupCodecTests
{
    // Synthetic values only; no real credentials are loaded by these tests.
    private sealed record Settings(string Language, string Token);
    private static readonly Settings Payload = new("ja-JP", "synthetic-token");
    private static string CreateBackup(string password = "p") => JsonSerializer.Serialize(
        ConfigBackupCodec.Encrypt(Payload, password, DateTimeOffset.UtcNow));

    [Theory]
    [InlineData("p")]
    [InlineData("  synthetic WebDAV password  ")]
    [InlineData("合成パスワード")]
    public void UsesExistingWebDavPasswordWithoutExtraLengthRule(string password)
    {
        var json = CreateBackup(password);
        Assert.DoesNotContain("synthetic-token", json);
        Assert.Equal(Payload, ConfigBackupCodec.Decrypt<Settings>(json, password));
    }

    [Fact]
    public void SameSettingsProduceDifferentCiphertext()
    {
        Assert.NotEqual(CreateBackup(), CreateBackup());
    }

    [Fact]
    public void ChangedWebDavPasswordCannotDecryptBackup()
    {
        var error = Assert.Throws<ConfigBackupException>(() => ConfigBackupCodec.Decrypt<Settings>(CreateBackup(), "changed"));
        Assert.Equal("InvalidPasswordOrBackup", error.Code);
    }

    [Fact]
    public void ModifiedCiphertextIsRejected()
    {
        var node = JsonNode.Parse(CreateBackup())!.AsObject();
        var bytes = Convert.FromBase64String(node["Ciphertext"]!.GetValue<string>());
        bytes[0] ^= 1;
        node["Ciphertext"] = Convert.ToBase64String(bytes);
        var error = Assert.Throws<ConfigBackupException>(() => ConfigBackupCodec.Decrypt<Settings>(node.ToJsonString(), "p"));
        Assert.Equal("InvalidPasswordOrBackup", error.Code);
    }

    [Theory]
    [InlineData("SchemaVersion", "1")]
    [InlineData("Iterations", "2000000000")]
    [InlineData("KeySource", "\"separate-password\"")]
    public void UnsupportedMetadataIsRejectedBeforeKeyDerivation(string property, string value)
    {
        var node = JsonNode.Parse(CreateBackup())!.AsObject();
        node[property] = JsonNode.Parse(value);
        var error = Assert.Throws<ConfigBackupException>(() => ConfigBackupCodec.Decrypt<Settings>(node.ToJsonString(), "p"));
        Assert.Equal("UnsupportedBackup", error.Code);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"not-base64\"")]
    public void MalformedEncryptionMetadataReturnsBackupError(string value)
    {
        var node = JsonNode.Parse(CreateBackup())!.AsObject();
        node["Nonce"] = JsonNode.Parse(value);
        var error = Assert.Throws<ConfigBackupException>(() => ConfigBackupCodec.Decrypt<Settings>(node.ToJsonString(), "p"));
        Assert.Equal("InvalidBackup", error.Code);
    }
}
