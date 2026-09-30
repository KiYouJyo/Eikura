using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Eizo;

internal sealed record ComponentHostContractDescriptor(
    string Name,
    Version MinVersion,
    Version MaxVersionExclusive)
{
    public bool Supports(string hostName, Version hostVersion) =>
        string.Equals(Name, hostName, StringComparison.Ordinal) &&
        hostVersion >= MinVersion &&
        hostVersion < MaxVersionExclusive;
}

internal sealed record ComponentDefinition(
    string Id,
    string DisplayName,
    string FolderName,
    string Repository,
    string ArchivePrefix,
    string ManifestAssetName,
    string HostContractName,
    Version HostContractVersion,
    string AnchorAssemblyName,
    IReadOnlyList<string> RequiredAssemblyNames);

internal sealed record ComponentReleaseManifest(
    string Component,
    Version Version,
    Version AbiVersion,
    string Tag,
    string Commit,
    string Runtime,
    string Framework,
    string SourceRepository,
    ComponentHostContractDescriptor HostContract);

internal sealed record ComponentPackageDescriptor(
    ComponentDefinition Definition,
    Version Version,
    Version AbiVersion,
    ComponentHostContractDescriptor HostContract,
    string DirectoryPath,
    IReadOnlyDictionary<string, string> RequiredAssemblies,
    IReadOnlyDictionary<string, string> ResolverAssemblies);

internal sealed record ComponentRuntimeStatus(
    ComponentDefinition Definition,
    Version BundledVersion,
    Version CurrentVersion,
    Version CurrentAbiVersion,
    bool IsUsingExternalComponent,
    Version? PendingVersion,
    string? LastActivationError);

internal static class EizoComponents
{
    public static readonly ComponentDefinition Playback = new(
        Id: "Eizo.Playback",
        DisplayName: "Playback",
        FolderName: "Playback",
        Repository: "KiYouJyo/Eikura.Playback",
        ArchivePrefix: "Eizo.Playback.Runtime",
        ManifestAssetName: "eizo-playback-release.json",
        HostContractName: "Eizo.Playback.Host",
        HostContractVersion: new Version(1, 0, 0),
        AnchorAssemblyName: "Eizo.Playback.LibVLC.WinUI",
        RequiredAssemblyNames:
        [
            "Eizo.Playback.Abstractions",
            "Eizo.Playback.Core",
            "Eizo.Playback.LibVLC",
            "Eizo.Playback.LibVLC.WinUI"
        ]);

    public static readonly ComponentDefinition Recognition = new(
        Id: "Eizo.Recognition",
        DisplayName: "Metadata",
        FolderName: "Recognition",
        Repository: "KiYouJyo/Eikura.Metadata",
        ArchivePrefix: "Eizo.Recognition.Runtime",
        ManifestAssetName: "eizo-recognition-release.json",
        HostContractName: "Eizo.Recognition.Host",
        HostContractVersion: new Version(1, 0, 0),
        AnchorAssemblyName: "Eizo.Metadata.Recognition",
        RequiredAssemblyNames:
        [
            "Eizo.Metadata.Recognition",
            "Eizo.Metadata.Core",
            "Eizo.Metadata.Providers"
        ]);

    public static IReadOnlyList<ComponentDefinition> All { get; } = [Playback, Recognition];
}

internal static class ComponentRuntimeBootstrapper
{
    private const string RootOverrideEnvironmentVariable = "EIZO_COMPONENTS_ROOT";
    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> ResolverAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ComponentRuntimeStatus> Statuses = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    public static string ComponentsRoot { get; } = ResolveComponentsRoot();

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized) return;
            _initialized = true;

            AssemblyLoadContext.Default.Resolving -= ResolveAssembly;
            AssemblyLoadContext.Default.Resolving += ResolveAssembly;

            foreach (var definition in EizoComponents.All)
                InitializeComponent(definition);
        }
    }

    public static ComponentRuntimeStatus GetStatus(ComponentDefinition definition)
    {
        Initialize();
        lock (Gate)
        {
            if (Statuses.TryGetValue(definition.Id, out var status)) return status;
            return new(definition, new Version(0, 0, 0), new Version(0, 0, 0), new Version(0, 0, 0), false, null, "Component runtime was not initialized.");
        }
    }

    public static string GetComponentRoot(ComponentDefinition definition) =>
        Path.Combine(ComponentsRoot, definition.FolderName);

    public static string GetVersionsRoot(ComponentDefinition definition) =>
        Path.Combine(GetComponentRoot(definition), "versions");

    public static string GetVersionDirectory(ComponentDefinition definition, Version version) =>
        Path.Combine(GetVersionsRoot(definition), FormatVersion(version));

    public static bool IsPendingVersion(ComponentDefinition definition, Version version) =>
        ReadState(GetPendingStatePath(definition)) is { } pending &&
        TryGetVersion(pending.Version, out var pendingVersion) &&
        pendingVersion == NormalizeVersion(version);

    public static void StageForNextLaunch(ComponentDefinition definition, Version version)
    {
        var normalized = NormalizeVersion(version);
        var directory = GetVersionDirectory(definition, normalized);
        if (!ComponentPackageValidator.TryValidate(definition, directory, out var package, out var error) || package is null || package.Version != normalized)
            throw new InvalidDataException(error ?? $"{definition.DisplayName} package cannot be staged.");
        if (!IsHostContractCompatible(definition, package.HostContract))
            throw new InvalidDataException($"{definition.DisplayName} host contract is incompatible with this Eikura build.");
        WriteState(GetPendingStatePath(definition), new ComponentActivationState(FormatVersion(normalized)));
        UpdatePendingStatus(definition, normalized);
    }

    public static void MarkActivationFailed(ComponentDefinition definition, string error)
    {
        lock (Gate)
        {
            TryDelete(GetActiveStatePath(definition));
            TryDelete(GetPendingStatePath(definition));
            if (Statuses.TryGetValue(definition.Id, out var status))
                Statuses[definition.Id] = status with { PendingVersion = null, LastActivationError = error };
        }
    }

    internal static bool IsHostContractCompatible(ComponentDefinition definition, ComponentHostContractDescriptor contract) =>
        contract.Supports(definition.HostContractName, definition.HostContractVersion);

    internal static Version NormalizeVersion(Version version) =>
        new(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));

    internal static string FormatVersion(Version version) =>
        $"{version.Major}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}";

    internal static bool TryReadFileProductVersion(string assemblyPath, out Version version)
    {
        version = new Version(0, 0, 0);
        var fileVersion = FileVersionInfo.GetVersionInfo(assemblyPath).FileVersion;
        if (!Version.TryParse(fileVersion, out var parsed) || parsed is null) return false;
        version = NormalizeVersion(parsed);
        return true;
    }

    private static void InitializeComponent(ComponentDefinition definition)
    {
        var componentRoot = GetComponentRoot(definition);
        var versionsRoot = GetVersionsRoot(definition);
        Directory.CreateDirectory(versionsRoot);

        var bundledAnchor = FindBundledAssembly(definition, definition.AnchorAssemblyName);
        var bundledVersion = bundledAnchor is not null && TryReadFileProductVersion(bundledAnchor, out var parsedBundled)
            ? parsedBundled
            : new Version(0, 0, 0);
        var bundledAbi = bundledAnchor is null
            ? new Version(0, 0, 0)
            : NormalizeAbiVersion(AssemblyName.GetAssemblyName(bundledAnchor).Version);

        var currentVersion = bundledVersion;
        var currentAbi = bundledAbi;
        var usingExternal = false;
        string? activationError = null;

        PromotePendingUpdate(definition, bundledVersion);

        var active = ReadState(GetActiveStatePath(definition));
        if (active is not null && TryGetVersion(active.Version, out var activeVersion) && activeVersion >= bundledVersion)
        {
            var directory = GetVersionDirectory(definition, activeVersion);
            if (ComponentPackageValidator.TryValidate(definition, directory, out var package, out var validationError) && package is not null)
            {
                if (IsHostContractCompatible(definition, package.HostContract))
                {
                    AddResolverAssemblies(package.ResolverAssemblies);
                    currentVersion = package.Version;
                    currentAbi = package.AbiVersion;
                    usingExternal = true;
                }
                else
                {
                    activationError = $"{definition.DisplayName} host contract mismatch.";
                    TryDelete(GetActiveStatePath(definition));
                }
            }
            else
            {
                activationError = validationError ?? $"The staged {definition.DisplayName} package is invalid.";
                TryDelete(GetActiveStatePath(definition));
            }
        }

        if (!usingExternal)
        {
            var bundledResolver = BuildBundledResolver(definition);
            AddResolverAssemblies(bundledResolver);
        }

        var pendingVersion = ReadState(GetPendingStatePath(definition)) is { } pending && TryGetVersion(pending.Version, out var pendingParsed)
            ? pendingParsed
            : null;

        Statuses[definition.Id] = new(
            definition,
            bundledVersion,
            currentVersion,
            currentAbi,
            usingExternal,
            pendingVersion,
            activationError);
    }

    private static void PromotePendingUpdate(ComponentDefinition definition, Version bundledVersion)
    {
        var pendingPath = GetPendingStatePath(definition);
        var pending = ReadState(pendingPath);
        if (pending is null) return;
        if (!TryGetVersion(pending.Version, out var version) || version < bundledVersion)
        {
            TryDelete(pendingPath);
            return;
        }

        var directory = GetVersionDirectory(definition, version);
        if (!ComponentPackageValidator.TryValidate(definition, directory, out var package, out _) ||
            package is null ||
            package.Version != version ||
            !IsHostContractCompatible(definition, package.HostContract))
        {
            TryDelete(pendingPath);
            return;
        }

        WriteState(GetActiveStatePath(definition), pending);
        TryDelete(pendingPath);
    }

    private static void UpdatePendingStatus(ComponentDefinition definition, Version version)
    {
        lock (Gate)
        {
            if (!Statuses.TryGetValue(definition.Id, out var status)) return;
            Statuses[definition.Id] = status with { PendingVersion = version };
        }
    }

    private static Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName.Name)) return null;
        lock (Gate)
        {
            if (!ResolverAssemblies.TryGetValue(assemblyName.Name, out var path)) return null;
            var loaded = context.Assemblies.FirstOrDefault(
                assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            if (loaded is not null) return loaded;
            if (!File.Exists(path)) return null;
            return context.LoadFromAssemblyPath(path);
        }
    }

    private static IReadOnlyDictionary<string, string> BuildBundledResolver(ComponentDefinition definition)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "Components", "Bundled", definition.FolderName);
        if (Directory.Exists(bundledRoot))
        {
            foreach (var dll in Directory.EnumerateFiles(bundledRoot, "*.dll", SearchOption.AllDirectories))
                map.TryAdd(Path.GetFileNameWithoutExtension(dll), Path.GetFullPath(dll));
            return map;
        }

        // Debug/source builds keep project outputs beside the host; Release builds relocate them.
        foreach (var assemblyName in definition.RequiredAssemblyNames)
        {
            var path = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
            if (File.Exists(path)) map[assemblyName] = Path.GetFullPath(path);
        }
        return map;
    }

    private static string? FindBundledAssembly(ComponentDefinition definition, string assemblyName)
    {
        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "Components", "Bundled", definition.FolderName);
        if (Directory.Exists(bundledRoot))
        {
            var relocated = Directory.EnumerateFiles(bundledRoot, $"{assemblyName}.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (relocated is not null) return relocated;
        }

        var direct = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
        return File.Exists(direct) ? direct : null;
    }

    private static void AddResolverAssemblies(IReadOnlyDictionary<string, string> assemblies)
    {
        foreach (var pair in assemblies)
            ResolverAssemblies[pair.Key] = pair.Value;
    }

    private static string ResolveComponentsRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(RootOverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot)) return Path.GetFullPath(overrideRoot);

        try
        {
            // Packaged WinUI must use the physical per-package LocalState store.
            // Environment.LocalApplicationData may be virtualized for packaged processes.
            var localState = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            if (!string.IsNullOrWhiteSpace(localState))
                return Path.Combine(localState, "Eizo", "Components");
        }
        catch
        {
            // Unpackaged/debug hosts fall back to classic LocalAppData.
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Eizo",
            "Components");
    }

    private static string GetPendingStatePath(ComponentDefinition definition) =>
        Path.Combine(GetComponentRoot(definition), "pending.json");

    private static string GetActiveStatePath(ComponentDefinition definition) =>
        Path.Combine(GetComponentRoot(definition), "active.json");

    private static ComponentActivationState? ReadState(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ComponentActivationState>(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void WriteState(string path, ComponentActivationState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception) when (File.Exists(path))
        {
            // Activation must always be able to fall back to the bundled component.
        }
    }

    private static bool TryGetVersion(string value, out Version version)
    {
        if (!Version.TryParse(value, out var parsed) || parsed is null)
        {
            version = new Version(0, 0, 0);
            return false;
        }
        version = NormalizeVersion(parsed);
        return true;
    }

    private static Version NormalizeAbiVersion(Version? version) =>
        version is null ? new Version(0, 0, 0) : NormalizeVersion(version);

    private sealed record ComponentActivationState(string Version);
}

internal static class ComponentReleaseManifestReader
{
    private const int SupportedSchemaVersion = 1;

    public static bool TryReadFile(
        ComponentDefinition definition,
        string path,
        out ComponentReleaseManifest? manifest,
        out string? error)
    {
        manifest = null;
        error = null;
        try
        {
            if (!File.Exists(path)) return Fail("Component release manifest is missing.", out error);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            if (!root.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out var schemaVersion) || schemaVersion != SupportedSchemaVersion)
                return Fail($"Unsupported component manifest schema. Expected {SupportedSchemaVersion}.", out error);
            if (!TryReadString(root, "component", out var component) || !string.Equals(component, definition.Id, StringComparison.Ordinal))
                return Fail("Component identity does not match the requested runtime.", out error);
            if (!TryReadVersion(root, "version", out var version)) return Fail("Component version is invalid.", out error);
            version = ComponentRuntimeBootstrapper.NormalizeVersion(version);
            if (!TryReadVersion(root, "abiVersion", out var abiVersion)) return Fail("Component ABI version is invalid.", out error);
            abiVersion = ComponentRuntimeBootstrapper.NormalizeVersion(abiVersion);
            if (!TryReadString(root, "tag", out var tag) || !string.Equals(tag, $"v{ComponentRuntimeBootstrapper.FormatVersion(version)}", StringComparison.OrdinalIgnoreCase))
                return Fail("Component tag does not match its version.", out error);
            if (!TryReadString(root, "commit", out var commit)) return Fail("Component source commit is missing.", out error);
            if (!TryReadString(root, "runtime", out var runtime) || !string.Equals(runtime, "x64", StringComparison.OrdinalIgnoreCase))
                return Fail("Component runtime is not x64.", out error);
            if (!TryReadString(root, "framework", out var framework) || !string.Equals(framework, "net10.0", StringComparison.OrdinalIgnoreCase))
                return Fail("Component framework is unsupported.", out error);
            if (!TryReadString(root, "sourceRepository", out var repository) || !string.Equals(repository, definition.Repository, StringComparison.OrdinalIgnoreCase))
                return Fail("Component source repository is invalid.", out error);
            if (!root.TryGetProperty("hostContract", out var hostContract) || hostContract.ValueKind != JsonValueKind.Object)
                return Fail("Component host contract is missing.", out error);
            if (!TryReadString(hostContract, "name", out var hostName)) return Fail("Host contract name is invalid.", out error);
            if (!TryReadVersion(hostContract, "minVersion", out var hostMin)) return Fail("Minimum host contract version is invalid.", out error);
            if (!TryReadVersion(hostContract, "maxVersionExclusive", out var hostMax)) return Fail("Maximum host contract version is invalid.", out error);
            if (hostMin >= hostMax) return Fail("Host contract range is invalid.", out error);

            manifest = new(
                component,
                version,
                abiVersion,
                tag,
                commit,
                runtime,
                framework,
                repository,
                new ComponentHostContractDescriptor(hostName, hostMin, hostMax));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryReadString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadVersion(JsonElement root, string name, out Version version)
    {
        version = new Version(0, 0, 0);
        return TryReadString(root, name, out var value) && Version.TryParse(value, out version!);
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}

internal static class ComponentPackageValidator
{
    public static bool TryValidate(
        ComponentDefinition definition,
        string directory,
        out ComponentPackageDescriptor? package,
        out string? error)
    {
        package = null;
        error = null;
        try
        {
            var fullDirectory = Path.GetFullPath(directory);
            var manifestPath = Path.Combine(fullDirectory, definition.ManifestAssetName);
            if (!ComponentReleaseManifestReader.TryReadFile(definition, manifestPath, out var manifest, out error) || manifest is null)
                return false;
            if (!ComponentRuntimeBootstrapper.IsHostContractCompatible(definition, manifest.HostContract))
                return Fail("Component host contract is incompatible with this Eikura build.", out error);

            var binRoot = Path.Combine(fullDirectory, "bin");
            if (!Directory.Exists(binRoot)) return Fail("Component bin payload is missing.", out error);

            var required = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assemblyName in definition.RequiredAssemblyNames)
            {
                var assemblyPath = Directory.EnumerateFiles(binRoot, $"{assemblyName}.dll", SearchOption.AllDirectories).FirstOrDefault();
                if (assemblyPath is null) return Fail($"Required component assembly is missing: {assemblyName}.dll", out error);
                _ = AssemblyName.GetAssemblyName(assemblyPath);
                required[assemblyName] = Path.GetFullPath(assemblyPath);
            }

            var anchor = required[definition.AnchorAssemblyName];
            if (!ComponentRuntimeBootstrapper.TryReadFileProductVersion(anchor, out var productVersion) || productVersion != manifest.Version)
                return Fail("Component product version does not match its release manifest.", out error);

            var resolver = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dll in Directory.EnumerateFiles(binRoot, "*.dll", SearchOption.AllDirectories))
                resolver.TryAdd(Path.GetFileNameWithoutExtension(dll), Path.GetFullPath(dll));
            foreach (var pair in required) resolver[pair.Key] = pair.Value;

            package = new(definition, manifest.Version, manifest.AbiVersion, manifest.HostContract, fullDirectory, required, resolver);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or BadImageFormatException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}
