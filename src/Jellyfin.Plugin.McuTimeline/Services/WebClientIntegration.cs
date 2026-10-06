using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// Hooks the timeline into the web client through two community plugins, both looked up by
/// reflection so the plugin starts without them. Plugin Pages gives the page its route.
/// File Transformation loads menu.js with the web client, which lists the page under Media.
/// </summary>
public sealed class WebClientIntegration : IHostedService
{
    /// <summary>
    /// Page id given to Plugin Pages.
    /// </summary>
    public const string PageId = "mcu-timeline";

    private const string PluginPagesInterface = "Jellyfin.Plugin.PluginPages.PluginInterface";
    private const string FileTransformationInterface = "Jellyfin.Plugin.FileTransformation.PluginInterface";

    // fixed, so a restart replaces the transformation instead of adding a second one
    private static readonly Guid _transformationId = Guid.Parse("4f0d7c39-2a8e-4b7e-9a51-6c1e0b8a2d17");

    private readonly ILogger<WebClientIntegration> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebClientIntegration"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public WebClientIntegration(ILogger<WebClientIntegration> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether menu.js is loaded with the web client. Plugin Pages
    /// then keeps the page out of its own menu section.
    /// </summary>
    public static bool MenuInjected { get; private set; }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        MenuInjected = Register(FileTransformationInterface, "RegisterTransformation", new Dictionary<string, string>
        {
            ["id"] = _transformationId.ToString(),
            ["fileNamePattern"] = "index.html",
            ["callbackAssembly"] = typeof(IndexTransformation).Assembly.FullName!,
            ["callbackClass"] = typeof(IndexTransformation).FullName!,
            ["callbackMethod"] = nameof(IndexTransformation.Transform)
        });

        // registered whatever the setting, Plugin Pages asks McuTimelinePageAvailability on
        // every menu load
        var pageRegistered = Register(PluginPagesInterface, "RegisterPage", new Dictionary<string, string>
        {
            ["id"] = PageId,
            ["url"] = "/McuTimeline/page",
            ["displayText"] = "Univers Marvel",
            ["icon"] = "timeline",
            ["isEnabledAssembly"] = typeof(Plugin).Assembly.GetName().Name!,
            ["isEnabledClass"] = nameof(McuTimelinePageAvailability),
            ["isEnabledMethod"] = nameof(McuTimelinePageAvailability.IsEnabled)
        });

        if (!pageRegistered)
        {
            _logger.LogInformation("[MCU Timeline] Plugin Pages not installed, the timeline has no page in the web client.");
        }
        else
        {
            _logger.LogInformation(
                "[MCU Timeline] Registered with Plugin Pages, menu entry {Where}.",
                MenuInjected ? "under Media" : "in the Plugin Pages section");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool Register(string interfaceType, string methodName, Dictionary<string, string> payload)
    {
        var type = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .Select(assembly => assembly.GetType(interfaceType, throwOnError: false))
            .FirstOrDefault(t => t is not null);
        var method = type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        var payloadType = method?.GetParameters().SingleOrDefault()?.ParameterType;

        // both plugins take a Newtonsoft JObject, built through its own Parse so this
        // plugin needs no Newtonsoft reference
        var parse = payloadType?.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
        if (method is null || parse is null)
        {
            return false;
        }

        try
        {
            method.Invoke(null, [parse.Invoke(null, [JsonSerializer.Serialize(payload)])]);
            return true;
        }
        catch (TargetInvocationException ex)
        {
            _logger.LogWarning(ex.InnerException ?? ex, "[MCU Timeline] {Interface}.{Method} failed.", interfaceType, methodName);
            return false;
        }
    }
}

/// <summary>
/// Called by Plugin Pages, through reflection, to decide whether its menu shows the timeline.
/// </summary>
public static class McuTimelinePageAvailability
{
    /// <summary>
    /// Tells whether Plugin Pages lists the page. Not when menu.js already lists it under Media.
    /// </summary>
    /// <param name="pageId">Page id, always <see cref="WebClientIntegration.PageId"/>.</param>
    /// <returns>Whether the page is listed.</returns>
    public static bool IsEnabled(string pageId) => PluginSettings.Current.ShowInPluginPages && !WebClientIntegration.MenuInjected;
}

/// <summary>
/// Called by File Transformation, through reflection, each time it serves index.html.
/// </summary>
public static class IndexTransformation
{
    // relative to /web/, so it follows any base URL
    private static readonly string _scriptTag =
        $"<script defer src=\"../McuTimeline/assets/menu.js?v={typeof(IndexTransformation).Assembly.GetName().Version}\"></script>";

    /// <summary>
    /// Adds menu.js to index.html.
    /// </summary>
    /// <param name="payload">The file, as File Transformation hands it over.</param>
    /// <returns>The new file content.</returns>
    public static string Transform(IndexPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var html = payload.Contents ?? string.Empty;
        var head = html.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return head < 0 || html.Contains(_scriptTag, StringComparison.Ordinal) ? html : html.Insert(head, _scriptTag);
    }
}

/// <summary>
/// What File Transformation passes to <see cref="IndexTransformation.Transform"/>.
/// </summary>
public sealed class IndexPayload
{
    /// <summary>
    /// Gets or sets the file content.
    /// </summary>
    public string? Contents { get; set; }
}
