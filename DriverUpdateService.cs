using System.IO.Compression;
using System.Globalization;
using System.Management;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HonorPCHelper;

internal sealed record DriverComponent(
    int Id,
    string Name,
    string DisplayName,
    string CurrentVersion,
    string PackageType,
    string? DeviceName = null,
    string? AppName = null);

internal sealed record DriverUpdate(
    DriverComponent Component,
    string Version,
    string VersionId,
    string DownloadBaseUrl,
    long Size,
    string? Sha256,
    string? ReleaseDate = null,
    bool IsUpdate = true,
    string? PackageTitle = null);

internal sealed record DriverCheckResult(
    IReadOnlyList<DriverComponent> Components,
    IReadOnlyList<DriverUpdate> Updates,
    IReadOnlyDictionary<int, string> AvailableVersions,
    bool IsComplete = true);

internal sealed class DriverUpdateService
{
    private const string CheckUrl =
        "https://update.platform.hihonorcloud.com/hid_and_common/v2/CheckEx.action?latest=true&verType=true&defenceHijack=true";
    private const string LaptopProductId = "CMCG10000120";
    private const int SupportRequestConcurrency = 6;

    // Driver endpoints must not inherit a stale local WinINET proxy.  A number
    // of VPN/proxy clients leave 127.0.0.1 configured after they stop, which
    // otherwise turns every update check into a timeout.
    private static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    internal async Task<DriverCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var installedComponents = await ReadInstalledComponentsAsync(cancellationToken);
        return await CheckAsync(installedComponents, cancellationToken);
    }

    internal async Task<IReadOnlyList<DriverComponent>> ReadInstalledComponentsAsync(
        CancellationToken cancellationToken = default)
        => await Task.Run(ReadInstalledComponents, cancellationToken);

    internal async Task<IReadOnlyList<DriverComponent>> BuildDeviceListAsync(
        CancellationToken cancellationToken = default)
        => AddMissingCatalogComponents(await ReadInstalledComponentsAsync(cancellationToken));

    internal async Task<DriverCheckResult> CheckAsync(
        IReadOnlyList<DriverComponent> installedComponents,
        CancellationToken cancellationToken = default)
    {
        var requestComponents = AddMissingCatalogComponents(installedComponents);
        var serverRequestComponents = requestComponents.Where(component => component.Id > 0).ToList();

        var machine = ReadMachineIdentity();
        var dashboard = string.Join(';', serverRequestComponents.Select(component => $"{component.Name}:{component.CurrentVersion}")) + ";";
        var request = new
        {
            components = serverRequestComponents.Select(component => new
            {
                AppName = component.AppName ?? component.Name,
                PackageName = component.Name,
                PackageType = component.PackageType,
                PackageVersionCode = component.CurrentVersion,
                PackageVersionName = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty,
                componentID = component.Id.ToString()
            }),
            rules = new
            {
                C_version = machine.CVersion,
                Dashboard = dashboard,
                DashboardFlash = string.Empty,
                DeviceName = machine.DeviceName,
                FirmWare = string.Empty,
                Language = System.Globalization.CultureInfo.CurrentUICulture.Name,
                OS = Environment.OSVersion.VersionString,
                deviceId = string.Empty,
                udid = string.Empty
            }
        };

        AppLog.Info($"Checking HONOR driver updates for {machine.DeviceName}/{machine.CVersion}; "
            + $"{installedComponents.Count} detected, {serverRequestComponents.Count} requested");

        var (apiUpdates, apiVersions) = await CheckPlatformAsync(
            request, serverRequestComponents, cancellationToken);
        var (siteComponents, siteUpdates, siteVersions, siteCheckComplete) = apiUpdates.Count > 0 || apiVersions.Count > 0
            ? (Array.Empty<DriverComponent>(), Array.Empty<DriverUpdate>(),
                (IReadOnlyDictionary<int, string>)new Dictionary<int, string>(), true)
            : await CheckSupportSiteAsync(machine, serverRequestComponents, cancellationToken);
        var updates = apiUpdates.Concat(siteUpdates)
            .GroupBy(update => update.Component.Id)
            .Select(group => group.OrderByDescending(update => VersionParts(update.Version),
                VersionPartComparer.Instance).First())
            .ToList();
        var availableVersions = apiVersions.ToDictionary(item => item.Key, item => item.Value);
        foreach (var item in siteVersions)
        {
            if (!availableVersions.TryGetValue(item.Key, out var version)
                || CompareVersions(item.Value, version) > 0)
                availableVersions[item.Key] = item.Value;
        }
        // Keep missing catalog entries visible even when HONOR has no compatible
        // package for this particular model. Previously those entries were used
        // in the request and then discarded unless the server returned a version,
        // which made devices without a detected driver disappear from the UI.
        var components = requestComponents.Concat(siteComponents)
            .GroupBy(component => component.Id)
            .Select(group => group.First())
            .ToList();
        AppLog.Info($"HONOR driver update check completed; {updates.Count} update(s)");
        return new DriverCheckResult(components, updates, availableVersions, siteCheckComplete);
    }

    private static async Task<(
        IReadOnlyList<DriverUpdate> Updates,
        IReadOnlyDictionary<int, string> AvailableVersions)> CheckPlatformAsync(
        object request,
        IReadOnlyList<DriverComponent> components,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // This endpoint is only the fast path.  Waiting half a minute here
            // makes the window look hung before the support catalog is even
            // tried, and the catalog contains the same packages.
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await Http.PostAsJsonAsync(CheckUrl, request, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            return ParseUpdates(document.RootElement, components);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Info("HONOR update platform request timed out; using support catalog");
            return ([], new Dictionary<int, string>());
        }
        catch (Exception exception)
        {
            AppLog.Error("HONOR update platform request failed; using support catalog", exception);
            return ([], new Dictionary<int, string>());
        }
    }

    internal async Task DownloadAsync(
        DriverUpdate update,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var safeVersion = string.Concat(update.Version.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var packageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HonorPCHelper", "DriverUpdates", $"{update.Component.Id}_{safeVersion}");

        var downloadUrl = GetDownloadUrl(update.DownloadBaseUrl);
        var originalPackageName = GetOriginalPackageName(downloadUrl)
            ?? SafeFileName(update.PackageTitle ?? update.Component.DisplayName);
        var extension = Path.GetExtension(downloadUrl.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
            extension = ".download";
        var saveExtension = extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".download", StringComparison.OrdinalIgnoreCase)
            ? ".exe"
            : extension;
        using var dialog = new SaveFileDialog
        {
            Title = L.T("Сохранить драйвер", "Save driver", "保存驱动程序"),
            FileName = Path.ChangeExtension(originalPackageName, saveExtension),
            Filter = $"{L.T("Файл драйвера", "Driver file", "驱动程序文件")} (*{saveExtension})|*{saveExtension}",
            AddExtension = true,
            DefaultExt = saveExtension.TrimStart('.'),
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != DialogResult.OK)
            throw new OperationCanceledException(cancellationToken);

        Directory.CreateDirectory(packageDirectory);
        var packagePath = Path.Combine(packageDirectory, "package" + extension);
        var extractDirectory = Path.Combine(packageDirectory, "package");
        if (Directory.Exists(extractDirectory))
            Directory.Delete(extractDirectory, recursive: true);

        AppLog.Info($"Downloading driver {update.Component.Name} {update.Version} from {downloadUrl.Host}");
        using (var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                if (total > 0)
                    progress?.Report((int)Math.Clamp(written * 100 / total.Value, 0, 100));
            }
        }

        if (!string.IsNullOrWhiteSpace(update.Sha256))
        {
            await using var package = File.OpenRead(packagePath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(package, cancellationToken));
            if (!actualHash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.T(
                    "Контрольная сумма пакета драйвера не совпадает.",
                    "The driver package checksum does not match.",
                    "驱动程序包校验和不匹配。"));
        }

        var fileToSave = packagePath;
        if (IsZipPackage(packagePath))
        {
            Directory.CreateDirectory(extractDirectory);
            ExtractSafely(packagePath, extractDirectory);
            ExtractNestedArchivesSafely(extractDirectory);
            var installer = Directory.GetFiles(extractDirectory, "*.exe", SearchOption.AllDirectories)
                .OrderByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault();
            if (installer is not null)
                fileToSave = installer;
        }
        var fromOfficialSupport = downloadUrl.Host.EndsWith("service.hihonor.com", StringComparison.OrdinalIgnoreCase)
            || downloadUrl.Host.EndsWith("honor.com", StringComparison.OrdinalIgnoreCase)
            || downloadUrl.Host.EndsWith("hihonorcloud.com", StringComparison.OrdinalIgnoreCase);
        if (Path.GetExtension(fileToSave).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            VerifyInstallerSignature(fileToSave, requireHonorPublisher: !fromOfficialSupport);
        else if (!fromOfficialSupport && string.IsNullOrWhiteSpace(update.Sha256))
            throw new InvalidDataException(L.T(
                "Не удалось проверить целостность пакета драйвера.",
                "The driver package integrity could not be verified.",
                "无法验证驱动程序包的完整性。"));

        var destinationPath = Path.ChangeExtension(dialog.FileName, Path.GetExtension(fileToSave));
        if (!destinationPath.Equals(dialog.FileName, StringComparison.OrdinalIgnoreCase)
            && File.Exists(destinationPath)
            && MessageBox.Show(
                L.T($"Файл {Path.GetFileName(destinationPath)} уже существует. Заменить его?",
                    $"The file {Path.GetFileName(destinationPath)} already exists. Replace it?",
                    $"文件 {Path.GetFileName(destinationPath)} 已存在。是否替换？"),
                dialog.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            throw new OperationCanceledException(cancellationToken);
        File.Copy(fileToSave, destinationPath, overwrite: true);
        AppLog.Info($"Saved verified driver package to {destinationPath}");
    }

    private static bool IsZipPackage(string path)
    {
        Span<byte> signature = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        return stream.Read(signature) == signature.Length
            && signature[0] == (byte)'P' && signature[1] == (byte)'K';
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var name = string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(name) ? "HONOR-driver" : name;
    }

    private static string? GetOriginalPackageName(Uri downloadUrl)
    {
        var pathName = Uri.UnescapeDataString(Path.GetFileName(downloadUrl.AbsolutePath));
        var encoded = Path.GetFileNameWithoutExtension(pathName);
        if (encoded.Length is > 0 and <= 8192)
        {
            try
            {
                var base64 = encoded.Replace('-', '+').Replace('_', '/');
                base64 += new string('=', (4 - base64.Length % 4) % 4);
                using var compressed = new MemoryStream(Convert.FromBase64String(base64));
                using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
                using var document = JsonDocument.Parse(gzip);
                var fileName = Text(document.RootElement, "fileName");
                if (!string.IsNullOrWhiteSpace(fileName))
                    return SafeFileName(Path.GetFileName(fileName));
            }
            catch (Exception exception) when (exception is FormatException
                or InvalidDataException or JsonException)
            {
                // Some support links use a regular file path instead of an encoded descriptor.
            }
        }

        return string.IsNullOrWhiteSpace(Path.GetExtension(pathName)) ? null : SafeFileName(pathName);
    }

    private static List<DriverComponent> ReadInstalledComponents()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceName, DeviceID, DriverVersion, DriverProviderName FROM Win32_PnPSignedDriver");
        using var results = searcher.Get();
        var drivers = results.Cast<ManagementObject>()
            .Select(item => new PnpDriver(
                Convert.ToString(item["DeviceName"]) ?? string.Empty,
                Convert.ToString(item["DeviceID"]) ?? string.Empty,
                Convert.ToString(item["DriverVersion"]) ?? string.Empty,
                Convert.ToString(item["DriverProviderName"]) ?? string.Empty))
            .Where(driver => !string.IsNullOrWhiteSpace(driver.Version))
            .ToArray();

        var components = new List<DriverComponent>();
        Add(components, drivers, 87, "VDisplay", "Virtual Display", d => Has(d, "virtual display"));
        Add(components, drivers, 88, "VHID", "Virtual HID", d => Has(d, "virtual hid"));
        Add(components, drivers, 12, "WDT", "Watchdog", d => Has(d, "wdtdevice") || IdHas(d, "WDT0001"));
        Add(components, drivers, 1, "Chipset", "Chipset", d => Has(d, "lpc/espi") || Has(d, "chipset"));
        Add(components, drivers, 2, "ME", "Intel Management Engine", d => Has(d, "management engine interface"));
        Add(components, drivers, 3, "Graphics", "Graphics", d =>
            IdHas(d, "VEN_8086") && (Has(d, "arc(tm)") || Has(d, "graphics"))
            || IdHas(d, "VEN_1002") && Has(d, "graphics"));
        Add(components, drivers, 4, "SerialIO", "Serial IO", d => Has(d, "serial io"));
        Add(components, drivers, 6, "DPTF", "Platform framework", d =>
            Has(d, "innovation platform framework manager")
            || Has(d, "dynamic platform and thermal framework"));
        Add(components, drivers, 14, "Audio", "Audio", d =>
            IdHas(d, "FUNC_01") && Has(d, "audio"));
        Add(components, drivers, 15, "WiFi", "Wi-Fi", d =>
            (d.DeviceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)
                || d.DeviceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
            && (Has(d, "wi-fi") || Has(d, "wireless"))
            && !Has(d, "virtual")
            && !Has(d, "software extension")
            && !d.Provider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase));
        Add(components, drivers, 16, "BT", "Bluetooth", d =>
            Has(d, "bluetooth") && !d.Provider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase));
        Add(components, drivers, 18, "EgisFingerPrint", "Fingerprint", d => Has(d, "fingerprint"), FingerprintPackageType);
        Add(components, drivers, 41, "Monitor", "Monitor", d =>
            (d.DeviceId.StartsWith("DISPLAY\\", StringComparison.OrdinalIgnoreCase)
                || d.DeviceId.StartsWith("MONITOR\\", StringComparison.OrdinalIgnoreCase))
            && !d.Provider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase));
        Add(components, drivers, 52, "NFC", "NFC", d => Has(d, "nfc") || IdHas(d, "NTAG"));
        Add(components, drivers, 55, "PPM", "Intel PPM", d => Has(d, "ppm provisioning"));
        Add(components, drivers, 56, "TXT", "Intel TXT", d => Has(d, "txt authenticated"));
        Add(components, drivers, 65, "ISST", "Intel Smart Sound", d => Has(d, "smart sound technology bus"));
        Add(components, drivers, 73, "iPMT", "Intel PMT", d => Has(d, "platform monitoring technology"));
        Add(components, drivers, 74, "NPU", "Intel NPU", d => Has(d, "intel(r) npu") || Has(d, "intel npu"));
        Add(components, drivers, 76, "LuxvisionsCameraDrv", "Camera", d =>
            Has(d, "camera") && !d.Provider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase),
            appName: "HonorCamera");
        Add(components, drivers, 78, "MEP", "Windows Studio Effects", d =>
            IdHas(d, "MEP_") && (Has(d, "windows camera effects") || Has(d, "windows studio effects")));

        using var biosSearcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
        using var biosResults = biosSearcher.Get();
        var biosVersion = biosResults.Cast<ManagementObject>()
            .Select(item => Convert.ToString(item["SMBIOSBIOSVersion"]))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var biosPackageType = Convert.ToString(
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName", string.Empty));
        if (!string.IsNullOrWhiteSpace(biosVersion))
            components.Add(new DriverComponent(23, "BIOS", "BIOS", biosVersion, biosPackageType ?? string.Empty));

        using var missingSearcher = new ManagementObjectSearcher(
            "SELECT Name, DeviceID, PNPClass FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 28");
        using var missingResults = missingSearcher.Get();
        var missingDeviceId = -1;
        foreach (var item in missingResults.Cast<ManagementObject>()
                     .OrderBy(item => Convert.ToString(item["DeviceID"]), StringComparer.OrdinalIgnoreCase))
        {
            var name = Convert.ToString(item["Name"]);
            var deviceId = Convert.ToString(item["DeviceID"]);
            var pnpClass = Convert.ToString(item["PNPClass"]);
            components.Add(new DriverComponent(
                missingDeviceId--,
                "MissingDevice",
                string.IsNullOrWhiteSpace(name)
                    ? L.T("Неизвестное устройство", "Unknown device", "未知设备")
                    : name,
                "0",
                pnpClass ?? string.Empty,
                deviceId));
        }

        return components.GroupBy(component => component.Id).Select(group => group.First()).ToList();
    }

    private static List<DriverComponent> AddMissingCatalogComponents(IReadOnlyCollection<DriverComponent> installed)
    {
        var result = installed.ToList();
        var installedIds = installed.Select(component => component.Id).ToHashSet();
        foreach (var item in ComponentCatalog)
        {
            if (!installedIds.Contains(item.Id))
                result.Add(new DriverComponent(
                    item.Id, item.Name, item.DisplayName, "0", string.Empty, AppName: item.AppName));
        }
        return result;
    }

    private static void Add(
        ICollection<DriverComponent> components,
        IEnumerable<PnpDriver> drivers,
        int id,
        string name,
        string displayName,
        Func<PnpDriver, bool> predicate,
        Func<PnpDriver, string>? packageType = null,
        string? appName = null)
    {
        var match = drivers.Where(predicate)
            .OrderByDescending(driver => VersionParts(driver.Version), VersionPartComparer.Instance)
            .FirstOrDefault();
        if (match is not null)
            components.Add(new DriverComponent(id, name, displayName, match.Version,
                packageType?.Invoke(match) ?? string.Empty, match.DeviceName, appName));
    }

    private static (
        IReadOnlyList<DriverUpdate> Updates,
        IReadOnlyDictionary<int, string> AvailableVersions) ParseUpdates(
        JsonElement root,
        IReadOnlyList<DriverComponent> components)
    {
        var result = new List<DriverUpdate>();
        var availableVersions = new Dictionary<int, string>();
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "0"
            || !root.TryGetProperty("components", out var serverComponents))
            return (result, availableVersions);

        var byId = components.ToDictionary(component => component.Id);
        foreach (var item in serverComponents.EnumerateArray())
        {
            if (!TryInt(item, "componentID", out var id) || !byId.TryGetValue(id, out var component))
                continue;
            var version = Text(item, "version") ?? Text(item, "name") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(version)
                && (!availableVersions.TryGetValue(id, out var availableVersion)
                    || CompareVersions(version, availableVersion) > 0))
                availableVersions[id] = version;
            var url = Text(item, "url") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(url)
                || CompareVersions(version, component.CurrentVersion) <= 0)
                continue;
            _ = long.TryParse(Text(item, "size"), out var size);
            result.Add(new DriverUpdate(component, version, Text(item, "versionID") ?? string.Empty,
                url, size, Text(item, "sha256") ?? Text(item, "hash"),
                NormalizeReleaseDate(Text(item, "releaseDate") ?? Text(item, "updateTime") ?? Text(item, "date"))));
        }
        return (result, availableVersions);
    }

    private static async Task<(
        IReadOnlyList<DriverComponent> Components,
        IReadOnlyList<DriverUpdate> Updates,
        IReadOnlyDictionary<int, string> AvailableVersions,
        bool IsComplete)> CheckSupportSiteAsync(
        MachineIdentity machine,
        IReadOnlyList<DriverComponent> components,
        CancellationToken cancellationToken)
    {
        var hadFailure = false;
        var knownOfferings = KnownSupportOfferings(machine);
        // ReadSupportPackagesAsync uses the same canonical catalog/package
        // endpoints for a known offering, so retrying it once per regional
        // product-tree endpoint only repeats an identical request.
        var endpoints = knownOfferings.Count > 0 ? SupportEndpoints.Take(1) : SupportEndpoints;
        foreach (var endpoint in endpoints)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                // HONOR's product-tree lookup is by far the slowest part of the
                // check and occasionally stalls.  Prefer a verified model to
                // offering mapping when one is available; package metadata is
                // still fetched live from the official catalog.
                var offerings = knownOfferings.Count > 0
                    ? knownOfferings
                    : await ResolveSupportOfferingsAsync(endpoint, machine, timeout.Token);
                if (offerings.Count == 0)
                    continue;

                var result = await ReadSupportPackagesAsync(
                    endpoint, offerings, components, timeout.Token);
                if (result.Updates.Count > 0)
                {
                    AppLog.Info($"Official HONOR support matched {string.Join(',', offerings)}; "
                        + $"{result.Updates.Count} package(s)");
                    return (result.Components, result.Updates, result.AvailableVersions, true);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                AppLog.Info($"HONOR support catalog {endpoint.CountryCode} timed out");
                hadFailure = true;
            }
            catch (Exception exception)
            {
                AppLog.Error($"HONOR support catalog {endpoint.CountryCode} failed", exception);
                hadFailure = true;
            }
        }
        AppLog.Info($"Official HONOR support has no catalog match for {machine.DeviceName}/{machine.CVersion}");
        return ([], [], new Dictionary<int, string>(), !hadFailure);
    }

    private static IReadOnlyList<string> KnownSupportOfferings(MachineIdentity machine)
    {
        // HONOR MagicBook Pro 14 2026 (ZQC-P / ZhuqueC, platform C233).
        // The public support catalog identifies this model by its offering code
        // rather than any of the identifiers exposed by Windows.
        var identifiers = machine.Identifiers
            .Append(machine.DeviceName)
            .Append(machine.CVersion)
            .Select(NormalizeIdentity)
            .ToHashSet(StringComparer.Ordinal);
        return identifiers.Contains("ZQCP")
               || identifiers.Contains("ZHUQUEC")
               || identifiers.Contains("C233")
            ? ["OFFE00461151"]
            : [];
    }

    private static async Task<IReadOnlyList<string>> ResolveSupportOfferingsAsync(
        SupportEndpoint endpoint,
        MachineIdentity machine,
        CancellationToken cancellationToken)
    {
        using var seriesDocument = await GetSupportJsonAsync(endpoint,
            "/ccpc/queryNewCommodityList/1000",
            new Dictionary<string, string>
            {
                ["productLevel"] = "lv3",
                ["productId"] = LaptopProductId
            }, cancellationToken);
        if (!TryProductList(seriesDocument.RootElement, out var series))
            return [];

        var productIds = series.EnumerateArray()
            .Select(item => Text(item, "productId"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToList();
        using var requestSlots = new SemaphoreSlim(SupportRequestConcurrency);
        var productTasks = productIds.Select(async productId =>
        {
            await requestSlots.WaitAsync(cancellationToken);
            try
            {
                using var productsDocument = await GetSupportJsonAsync(endpoint,
                    "/ccpc/queryNewCommodityList/1000",
                    new Dictionary<string, string>
                    {
                        ["productLevel"] = "lv5",
                        ["productId"] = productId
                    }, cancellationToken);
                if (!TryProductList(productsDocument.RootElement, out var productList))
                    return Array.Empty<SupportProduct>();
                return productList.EnumerateArray()
                    .Select(item => new SupportProduct(
                        Text(item, "offeringCode") ?? Text(item, "productId") ?? string.Empty,
                        string.Join(' ', new[]
                        {
                            Text(item, "displayName"), Text(item, "productName"),
                            Text(item, "backName"), Text(item, "keyword")
                        }.Where(value => !string.IsNullOrWhiteSpace(value)))))
                    .Where(product => !string.IsNullOrWhiteSpace(product.OfferingCode))
                    .ToArray();
            }
            finally
            {
                requestSlots.Release();
            }
        });
        var products = (await Task.WhenAll(productTasks)).SelectMany(items => items).ToList();

        var scored = products.Select(product => (Product: product, Score: ScoreProduct(product, machine)))
            .Where(item => item.Score >= 100)
            .OrderByDescending(item => item.Score)
            .ToList();
        if (scored.Count == 0)
            return [];
        var bestScore = scored[0].Score;
        if (bestScore < 130 && scored.Count(item => item.Score == bestScore) > 1
            && ProcessorIdentityTokens(machine.ProcessorName).Count > 0)
            return [];
        return scored.Where(item => item.Score == bestScore)
            .Select(item => item.Product.OfferingCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ScoreProduct(SupportProduct product, MachineIdentity machine)
    {
        var productText = NormalizeIdentity(product.Description);
        var score = 0;
        foreach (var identifier in machine.Identifiers)
        {
            var normalized = NormalizeIdentity(identifier);
            if (normalized.Length >= 4 && productText.Contains(normalized, StringComparison.Ordinal))
            {
                var hardwareCode = !identifier.Any(char.IsWhiteSpace)
                    && (identifier.Contains('-') || identifier.Any(char.IsDigit));
                score = Math.Max(score, (hardwareCode ? 100 : 70) + Math.Min(normalized.Length, 20));
            }
        }
        if (score < 70)
            return 0;

        foreach (var token in ProcessorIdentityTokens(machine.ProcessorName))
        {
            if (productText.Contains(token, StringComparison.Ordinal))
                score += 30;
        }
        if (machine.MemoryGb > 0
            && Regex.IsMatch(product.Description, $@"\b{machine.MemoryGb}\s*GB\b", RegexOptions.IgnoreCase))
            score += 5;
        return score;
    }

    private static string NormalizeIdentity(string value)
        => Regex.Replace(value, "[^A-Z0-9]", string.Empty, RegexOptions.IgnoreCase).ToUpperInvariant();

    private static IReadOnlyList<string> ProcessorIdentityTokens(string value)
        => Regex.Matches(value, @"\b[A-Z0-9]*\d[A-Z0-9-]{2,}\b", RegexOptions.IgnoreCase)
            .Select(match => NormalizeIdentity(match.Value))
            .Where(token => token.Length >= 4 && token.Any(char.IsLetter) && token.Any(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static async Task<(
        IReadOnlyList<DriverComponent> Components,
        IReadOnlyList<DriverUpdate> Updates,
        IReadOnlyDictionary<int, string> AvailableVersions)> ReadSupportPackagesAsync(
        SupportEndpoint endpoint,
        IReadOnlyList<string> offeringCodes,
        IReadOnlyList<DriverComponent> installedComponents,
        CancellationToken cancellationToken)
    {
        var catalogEndpoint = SupportEndpoints[0];
        var packageEndpoint = ChinesePackageEndpoint;
        var byId = installedComponents.ToDictionary(component => component.Id);
        var discovered = new Dictionary<int, DriverComponent>();
        var updates = new List<DriverUpdate>();
        var versions = new Dictionary<int, string>();
        foreach (var offeringCode in offeringCodes)
        {
            using var catalogDocument = await GetSupportJsonAsync(catalogEndpoint,
                "/knowledgeBase/getKnowCatalog/1000",
                new Dictionary<string, string>
                {
                    ["knowTypeId"] = "63",
                    ["orderType"] = "1",
                    ["page"] = "1",
                    ["pageSize"] = "100",
                    ["offeringCode"] = offeringCode,
                    ["cflag"] = "1"
                }, cancellationToken);
            if (!catalogDocument.RootElement.TryGetProperty("responseData", out var catalogData)
                || !catalogData.TryGetProperty("catalogList", out var catalogList))
                continue;

            var categories = catalogList.EnumerateArray()
                .Select(category => new SupportCategory(
                    Text(category, "knowTypeId") ?? string.Empty,
                    Text(category, "knowTypeName") ?? string.Empty))
                .Where(category => !string.IsNullOrWhiteSpace(category.Id))
                .ToList();
            using var requestSlots = new SemaphoreSlim(SupportRequestConcurrency);
            var packageTasks = categories.Select(async category =>
            {
                await requestSlots.WaitAsync(cancellationToken);
                try
                {
                    using var policyDocument = await GetSupportJsonAsync(packageEndpoint,
                        "/knowledgeBase/getServicePolicy/1000",
                        new Dictionary<string, string>
                        {
                            ["knowTypeId"] = category.Id,
                            ["orderType"] = "1",
                            ["page"] = "1",
                            ["pageSize"] = "999",
                            ["offeringCode"] = offeringCode,
                            ["driverDown"] = "true",
                            ["cflag"] = "1"
                        }, cancellationToken);
                    if (!policyDocument.RootElement.TryGetProperty("responseData", out var data)
                        || !data.TryGetProperty("detailList", out var details))
                        return Array.Empty<SupportPackage>();
                    return details.EnumerateArray()
                        .Select(item => new SupportPackage(
                            Text(item, "resourceTitle") ?? category.Name,
                            NormalizeSupportVersion(Text(item, "versionNumber") ?? string.Empty),
                            Text(item, "downloadUrl") ?? string.Empty,
                            Text(item, "updateTime"), category.Id, category.Name))
                        .Where(package => !string.IsNullOrWhiteSpace(package.Version)
                            && !string.IsNullOrWhiteSpace(package.Url))
                        .ToArray();
                }
                finally
                {
                    requestSlots.Release();
                }
            });
            foreach (var package in (await Task.WhenAll(packageTasks)).SelectMany(items => items))
            {
                var id = SupportComponentId(package.Title, package.CategoryId, package.CategoryName);
                DriverComponent component;
                if (id > 0 && byId.TryGetValue(id, out var installed))
                    component = installed;
                else
                {
                    if (id <= 0)
                        id = StableSupportComponentId(package.CategoryId + "|" + package.Title);
                    var catalogItem = ComponentCatalog.FirstOrDefault(entry => entry.Id == id);
                    component = new DriverComponent(id, catalogItem?.Name ?? package.Title,
                        catalogItem?.DisplayName ?? package.CategoryName, "0", string.Empty,
                        NormalizeSupportTitle(package.Title, package.Version), catalogItem?.AppName);
                    discovered.TryAdd(id, component);
                }
                if (!versions.TryGetValue(id, out var known) || CompareVersions(package.Version, known) > 0)
                    versions[id] = package.Version;
                updates.Add(new DriverUpdate(component, package.Version, string.Empty, package.Url, 0, null,
                    NormalizeReleaseDate(package.UpdateTime),
                    IsNewerVersion(package.Version, component.CurrentVersion),
                    NormalizeSupportTitle(package.Title, package.Version)));
            }
        }
        return (discovered.Values.ToList(), updates, versions);
    }

    private static async Task<JsonDocument> GetSupportJsonAsync(
        SupportEndpoint endpoint,
        string path,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var allParameters = parameters.ToDictionary(item => item.Key, item => item.Value);
        allParameters["areaCode"] = endpoint.CountryCode;
        allParameters["langCode"] = endpoint.Language;
        if (!endpoint.MinimalParameters)
        {
            allParameters["channelCode"] = "HONOR";
            allParameters["countryCode"] = endpoint.CountryCode;
            allParameters["country"] = endpoint.CountryCode;
            allParameters["language"] = endpoint.Language;
            allParameters["siteCode"] = endpoint.SiteCode;
        }
        allParameters["jsonp"] = "callback";
        var query = string.Join('&', allParameters.Select(item =>
            $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
        var jsonp = await Http.GetStringAsync($"{endpoint.ApiUrl}{path}?{query}", cancellationToken);
        var start = jsonp.IndexOf('(');
        var end = jsonp.LastIndexOf(')');
        if (start < 0 || end <= start)
            throw new InvalidDataException("HONOR support returned invalid JSONP.");
        return JsonDocument.Parse(jsonp[(start + 1)..end]);
    }

    private static bool TryProductList(JsonElement root, out JsonElement productList)
    {
        productList = default;
        return Text(root, "responseCode") == "200"
            && root.TryGetProperty("responseData", out var data)
            && data.TryGetProperty("productList", out productList);
    }

    private static int StableSupportComponentId(string value)
    {
        uint hash = 2166136261;
        foreach (var character in value)
            hash = (hash ^ char.ToUpperInvariant(character)) * 16777619;
        return 1000 + (int)(hash % 1_000_000);
    }

    private static int SupportComponentId(string title, string category, string categoryName)
    {
        var text = title + " " + categoryName;
        if (text.Contains("Camera", StringComparison.OrdinalIgnoreCase)) return 76;
        if (text.Contains("ISST", StringComparison.OrdinalIgnoreCase)) return 65;
        if (text.Contains("IntelME", StringComparison.OrdinalIgnoreCase)) return 2;
        if (text.Contains("Monitor", StringComparison.OrdinalIgnoreCase)) return 41;
        if (text.Contains("MEP", StringComparison.OrdinalIgnoreCase)) return 78;
        if (text.Contains("SerialIO", StringComparison.OrdinalIgnoreCase)) return 4;
        if (text.Contains("VHID", StringComparison.OrdinalIgnoreCase)) return 88;
        if (text.Contains("DPTF", StringComparison.OrdinalIgnoreCase)) return 6;
        if (text.Contains("PPM", StringComparison.OrdinalIgnoreCase)) return 55;
        if (text.Contains("TXT", StringComparison.OrdinalIgnoreCase)) return 56;
        if (text.Contains("NPU", StringComparison.OrdinalIgnoreCase)) return 74;
        if (text.Contains("NFC", StringComparison.OrdinalIgnoreCase)) return 52;
        if (text.Contains("iPMT", StringComparison.OrdinalIgnoreCase)) return 73;
        if (text.Contains("VDisplay", StringComparison.OrdinalIgnoreCase)) return 87;
        if (text.Contains("WDT", StringComparison.OrdinalIgnoreCase)) return 12;
        if (category == "631") return 1;
        if (category == "633") return 3;
        if (category == "634") return 15;
        if (category == "635") return 16;
        if (category == "637") return 18;
        if (category == "639") return 14;
        if (category == "6310") return 23;
        return 0;
    }

    private static string NormalizeSupportVersion(string value)
    {
        var dotted = System.Text.RegularExpressions.Regex.Matches(value, @"\d+(?:\.\d+)+")
            .Select(match => match.Value)
            .LastOrDefault();
        if (!string.IsNullOrWhiteSpace(dotted))
            return dotted;
        var numeric = System.Text.RegularExpressions.Regex.Matches(value, @"\d{6,}")
            .Select(match => match.Value)
            .LastOrDefault();
        return string.IsNullOrWhiteSpace(numeric) ? value.Trim() : numeric;
    }

    private static string? NormalizeReleaseDate(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    private static string NormalizeSupportTitle(string title, string version)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            title, @"[\s_]*Firmware\s*$", string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim(' ', '_');
        if (!string.IsNullOrWhiteSpace(version))
        {
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized, @"[\s_]*" + System.Text.RegularExpressions.Regex.Escape(version) + @"\s*$", string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim(' ', '_');
        }
        return normalized;
    }

    private static MachineIdentity ReadMachineIdentity()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\HONOR\BIOS");
        var deviceName = Convert.ToString(key?.GetValue("DeviceTypeEx"));
        var cVersion = Convert.ToString(key?.GetValue("CVersion"));
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddIdentity(identifiers, deviceName);
        AddIdentity(identifiers, cVersion);

        using var systemSearcher = new ManagementObjectSearcher(
            "SELECT Model, SystemFamily, SystemSKUNumber, TotalPhysicalMemory FROM Win32_ComputerSystem");
        using var systemResults = systemSearcher.Get();
        var system = systemResults.Cast<ManagementObject>().FirstOrDefault();
        var systemModel = Convert.ToString(system?["Model"]);
        var systemSku = Convert.ToString(system?["SystemSKUNumber"]);
        AddIdentity(identifiers, systemModel);
        AddIdentity(identifiers, Convert.ToString(system?["SystemFamily"]));
        AddIdentity(identifiers, systemSku);

        using var productSearcher = new ManagementObjectSearcher(
            "SELECT Name, Version, SKUNumber FROM Win32_ComputerSystemProduct");
        using var productResults = productSearcher.Get();
        foreach (var product in productResults.Cast<ManagementObject>())
        {
            AddIdentity(identifiers, Convert.ToString(product["Name"]));
            AddIdentity(identifiers, Convert.ToString(product["Version"]));
            AddIdentity(identifiers, Convert.ToString(product["SKUNumber"]));
        }

        using var boardSearcher = new ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");
        using var boardResults = boardSearcher.Get();
        foreach (var board in boardResults.Cast<ManagementObject>())
            AddIdentity(identifiers, Convert.ToString(board["Product"]));

        if (string.IsNullOrWhiteSpace(deviceName))
            deviceName = systemModel ?? Convert.ToString(Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName", "HONOR"));
        if (string.IsNullOrWhiteSpace(cVersion))
            cVersion = systemSku ?? Convert.ToString(Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "SystemSKU", string.Empty));
        using var cpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
        using var cpuResults = cpuSearcher.Get();
        var processor = cpuResults.Cast<ManagementObject>()
            .Select(item => Convert.ToString(item["Name"]))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        _ = ulong.TryParse(Convert.ToString(system?["TotalPhysicalMemory"]), out var memoryBytes);
        return new MachineIdentity(
            string.IsNullOrWhiteSpace(deviceName) ? "HONOR" : deviceName,
            cVersion ?? string.Empty,
            processor,
            (int)Math.Round(memoryBytes / 1024d / 1024d / 1024d),
            identifiers.ToList());
    }

    private static void AddIdentity(ISet<string> identifiers, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && !value.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("Default string", StringComparison.OrdinalIgnoreCase))
            identifiers.Add(value.Trim());
    }

    internal static Uri GetDownloadUrl(string baseUrl)
    {
        var builder = new UriBuilder(baseUrl) { Scheme = Uri.UriSchemeHttps, Port = -1 };
        var extension = Path.GetExtension(builder.Path);
        if (string.IsNullOrWhiteSpace(extension)
            && builder.Host.EndsWith("hihonorcloud.com", StringComparison.OrdinalIgnoreCase))
            builder.Path = builder.Path.TrimEnd('/') + "/full/update.zip";
        return builder.Uri;
    }

    private static void ExtractSafely(string archivePath, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The driver package contains an unsafe path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void ExtractNestedArchivesSafely(string rootDirectory)
    {
        var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var depth = 0; depth < 4; depth++)
        {
            var archives = Directory.GetFiles(rootDirectory, "*.zip", SearchOption.AllDirectories)
                .Where(path => extracted.Add(Path.GetFullPath(path)))
                .ToArray();
            if (archives.Length == 0)
                return;
            foreach (var archive in archives)
            {
                var destination = Path.Combine(Path.GetDirectoryName(archive)!,
                    Path.GetFileNameWithoutExtension(archive));
                Directory.CreateDirectory(destination);
                ExtractSafely(archive, destination);
            }
        }
    }

    private static void VerifyInstallerSignature(string path, bool requireHonorPublisher)
    {
        if (!WinTrust.IsSignatureValid(path))
            throw new InvalidDataException(L.T(
                "Цифровая подпись установщика недействительна.",
                "The installer digital signature is invalid.",
                "安装程序数字签名无效。"));

#pragma warning disable SYSLIB0057 // Needed to read the signer of an already WinVerifyTrust-validated file.
        using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
        var subject = certificate.Subject;
        if (requireHonorPublisher
            && !subject.Contains("Honor Device", StringComparison.OrdinalIgnoreCase)
            && !subject.Contains("Huawei", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.T(
                "Установщик подписан неизвестным издателем.",
                "The installer is signed by an unknown publisher.",
                "安装程序由未知发布者签名。"));
    }

    private static bool Has(PnpDriver driver, string value)
        => driver.DeviceName.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static bool IdHas(PnpDriver driver, string value)
        => driver.DeviceId.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static string FingerprintPackageType(PnpDriver driver)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            driver.DeviceId, @"VID_[0-9A-F]{4}&PID_[0-9A-F]{4}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : string.Empty;
    }

    private static int CompareVersions(string left, string right)
    {
        var a = VersionParts(left);
        var b = VersionParts(right);
        for (var index = 0; index < Math.Max(a.Length, b.Length); index++)
        {
            var av = index < a.Length ? a[index] : 0;
            var bv = index < b.Length ? b[index] : 0;
            if (av != bv)
                return av.CompareTo(bv);
        }
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNewerVersion(string available, string installed)
    {
        if (installed == "0")
            return true;

        // The support site often exposes a package build date (for example
        // 20260619), while Windows reports the actual driver version
        // (for example 10.18.26100.3). These values are not comparable.
        var availableIsDateCode = Regex.IsMatch(available, @"^20\d{6}$");
        var installedIsDateCode = Regex.IsMatch(installed, @"^20\d{6}$");
        return availableIsDateCode == installedIsDateCode
            && CompareVersions(available, installed) > 0;
    }

    private static int[] VersionParts(string value)
        => System.Text.RegularExpressions.Regex.Matches(value, @"\d+")
            .Select(match => int.TryParse(match.Value, out var part) ? part : 0)
            .ToArray();

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? value.ToString() : null;

    private static bool TryInt(JsonElement element, string name, out int value)
        => int.TryParse(Text(element, name), out value);

    private sealed record PnpDriver(string DeviceName, string DeviceId, string Version, string Provider);
    private sealed record MachineIdentity(
        string DeviceName,
        string CVersion,
        string ProcessorName,
        int MemoryGb,
        IReadOnlyList<string> Identifiers);
    private sealed record SupportEndpoint(
        string ApiUrl,
        string CountryCode,
        string Language,
        string SiteCode,
        bool MinimalParameters = false);
    private sealed record SupportProduct(string OfferingCode, string Description);
    private sealed record SupportCategory(string Id, string Name);
    private sealed record SupportPackage(
        string Title,
        string Version,
        string Url,
        string? UpdateTime,
        string CategoryId,
        string CategoryName);
    private sealed record CatalogItem(int Id, string Name, string DisplayName, string? AppName = null);

    private static readonly SupportEndpoint[] SupportEndpoints =
    [
        new("https://selfservice-ap.honor.com/ccpcmd/services/dispatch/secured/CCPC/EN", "UZ", "ru", "ru_UZ_X"),
        new("https://selfservice-eu.honor.com/ccpcmd/services/dispatch/secured/CCPC/EN", "GB", "en", "en_GB_X"),
        new("https://selfservice-cn.honor.com/ccpcmd/services/dispatch/secured/CCPC/EN", "CN", "zh_cn", "zh_CN_X")
    ];

    private static readonly SupportEndpoint ChinesePackageEndpoint = new(
        "https://selfservice-cn.honor.com/ccpcmd/services/dispatch/secured/CCPC/EN",
        "CN", "zh-cn", string.Empty, MinimalParameters: true);

    private static readonly CatalogItem[] ComponentCatalog =
    [
        new(1, "Chipset", "Chipset"),
        new(2, "ME", "Intel Management Engine"),
        new(3, "Graphics", "Graphics"),
        new(4, "SerialIO", "Serial IO"),
        new(6, "DPTF", "Platform framework"),
        new(12, "WDT", "Watchdog"),
        new(14, "Audio", "Audio"),
        new(15, "WiFi", "Wi-Fi"),
        new(16, "BT", "Bluetooth"),
        new(18, "EgisFingerPrint", "Fingerprint"),
        new(23, "BIOS", "BIOS"),
        new(41, "Monitor", "Monitor"),
        new(52, "NFC", "NFC"),
        new(55, "PPM", "Intel PPM"),
        new(56, "TXT", "Intel TXT"),
        new(65, "ISST", "Intel Smart Sound"),
        new(73, "iPMT", "Intel PMT"),
        new(74, "NPU", "Intel NPU"),
        new(76, "LuxvisionsCameraDrv", "Camera", "HonorCamera"),
        new(78, "MEP", "Windows Studio Effects"),
        new(87, "VDisplay", "Virtual Display"),
        new(88, "VHID", "Virtual HID")
    ];

    private sealed class VersionPartComparer : IComparer<int[]>
    {
        internal static readonly VersionPartComparer Instance = new();
        public int Compare(int[]? x, int[]? y)
        {
            x ??= [];
            y ??= [];
            for (var index = 0; index < Math.Max(x.Length, y.Length); index++)
            {
                var difference = (index < x.Length ? x[index] : 0).CompareTo(index < y.Length ? y[index] : 0);
                if (difference != 0)
                    return difference;
            }
            return 0;
        }
    }
}

internal static class WinTrust
{
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    internal static bool IsSignatureValid(string filePath)
    {
        var fileInfo = new WinTrustFileInfo(filePath);
        var data = new WinTrustData(fileInfo);
        try
        {
            return WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, data) == 0;
        }
        finally
        {
            data.Dispose();
            fileInfo.Dispose();
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo : IDisposable
    {
        private readonly uint StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        private IntPtr FilePath;
        private readonly IntPtr FileHandle = IntPtr.Zero;
        private readonly IntPtr KnownSubject = IntPtr.Zero;

        internal WinTrustFileInfo(string path) => FilePath = Marshal.StringToCoTaskMemUni(path);

        public void Dispose()
        {
            if (FilePath != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(FilePath);
                FilePath = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustData : IDisposable
    {
        private readonly uint StructSize = (uint)Marshal.SizeOf<WinTrustData>();
        private readonly IntPtr PolicyCallbackData = IntPtr.Zero;
        private readonly IntPtr SIPClientData = IntPtr.Zero;
        private readonly uint UIChoice = 2;
        private readonly uint RevocationChecks = 0;
        private readonly uint UnionChoice = 1;
        private IntPtr FileInfoPtr;
        private readonly uint StateAction = 0;
        private readonly IntPtr StateData = IntPtr.Zero;
        private readonly string? URLReference = null;
        private readonly uint ProviderFlags = 0x00000040;
        private readonly uint UIContext = 0;

        internal WinTrustData(WinTrustFileInfo fileInfo)
        {
            FileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, FileInfoPtr, false);
        }

        public void Dispose()
        {
            if (FileInfoPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(FileInfoPtr);
                FileInfoPtr = IntPtr.Zero;
            }
        }
    }
}
