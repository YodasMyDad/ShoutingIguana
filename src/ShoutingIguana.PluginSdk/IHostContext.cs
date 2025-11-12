using Microsoft.Extensions.Logging;

namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Context provided to plugins during initialization.
/// Allows plugins to register their tasks and exporters, and access application services.
/// </summary>
/// <remarks>
/// <para>
/// The host context is your plugin's gateway to the application. Use it to:
/// - Register URL analysis tasks
/// - Register custom export providers
/// - Create loggers for debugging
/// - Access the dependency injection container
/// - Get a repository accessor for querying crawled data
/// </para>
/// </remarks>
/// <example>
/// <para><b>Basic plugin initialization:</b></para>
/// <code>
/// [Plugin(Id = "com.example.myplugin", Name = "My Plugin")]
/// public class MyPlugin : IPlugin
/// {
///     public string Id => "com.example.myplugin";
///     public string Name => "My Plugin";
///     public Version Version => new(1, 0, 0);
///     public string Description => "Analyzes pages for my custom SEO checks";
///     
///     public void Initialize(IHostContext context)
///     {
///         // Create a logger
///         var logger = context.CreateLogger&lt;MyTask&gt;();
///         
///         // Register your analysis task
///         context.RegisterTask(new MyTask(logger));
///         
///         // Optionally register a custom exporter
///         // context.RegisterExport(new MyExporter(logger));
///     }
/// }
/// </code>
/// 
/// <para><b>Using service provider for advanced scenarios:</b></para>
/// <code>
/// public void Initialize(IHostContext context)
/// {
///     var serviceProvider = context.GetServiceProvider();
///     var logger = context.CreateLogger(nameof(MyTask));
///     
///     // Access repository for initialization logic
///     var accessor = context.GetRepositoryAccessor();
///     
///     context.RegisterTask(new MyTask(logger, accessor));
/// }
/// </code>
/// </example>
public interface IHostContext
{
    /// <summary>
    /// Registers a URL task that will be executed for each crawled URL.
    /// </summary>
    /// <param name="task">The task to register. Should implement <see cref="IUrlTask"/> or extend <see cref="UrlTaskBase"/>.</param>
    /// <remarks>
    /// <para>
    /// Tasks are executed in priority order (lower priority values run first).
    /// Default priority is 100. Set lower values (e.g., 10) for tasks that need to run early,
    /// or higher values (e.g., 200) for tasks that depend on others.
    /// </para>
    /// <para>
    /// Tasks execute asynchronously and can run in parallel. Ensure your task is thread-safe
    /// if it maintains any shared state.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// context.RegisterTask(new MyAnalysisTask(logger));
    /// </code>
    /// </example>
    void RegisterTask(IUrlTask task);
    
    /// <summary>
    /// Registers an export provider for custom file format exports.
    /// </summary>
    /// <param name="export">The export provider to register.</param>
    /// <remarks>
    /// <para>
    /// <b>Important:</b> Plugin findings are automatically exported to CSV/Excel/PDF.
    /// Only register an export provider if you need a specialized format like XML, JSON,
    /// or custom binary formats (e.g., sitemap.xml, structured JSON reports).
    /// </para>
    /// <para>
    /// For 95% of plugins, you don't need to implement <see cref="IExportProvider"/>.
    /// Just report findings via <c>ctx.Reports.ReportAsync()</c> and they'll be available
    /// in all standard exports automatically.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Only needed for specialized exports
    /// context.RegisterExport(new SitemapXmlExporter(logger, serviceProvider));
    /// </code>
    /// </example>
    void RegisterExport(IExportProvider export);
    
    /// <summary>
    /// Registers a report schema that defines custom columns for a plugin's report data.
    /// </summary>
    /// <param name="schema">The report schema defining columns and metadata.</param>
    /// <remarks>
    /// <para>
    /// Call this during plugin initialization to define custom columns for your plugin's reports.
    /// Once registered, use <c>ctx.Reports.ReportAsync()</c> in your task to submit data rows.
    /// </para>
    /// <para>
    /// The schema's TaskKey must match your IUrlTask.Key value.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public void Initialize(IHostContext context)
    /// {
    ///     var schema = ReportSchema.Create("BrokenLinks")
    ///         .AddPrimaryColumn("SourceUrl", ReportColumnType.Url)
    ///         .AddColumn("BrokenUrl", ReportColumnType.Url)
    ///         .AddColumn("StatusCode", ReportColumnType.Integer)
    ///         .AddColumn("LinkText", ReportColumnType.String)
    ///         .Build();
    ///     
    ///     context.RegisterReportSchema(schema);
    ///     context.RegisterTask(new BrokenLinksTask(logger));
    /// }
    /// </code>
    /// </example>
    void RegisterReportSchema(IReportSchema schema);
    
    /// <summary>
    /// Creates a logger instance for the plugin with the specified category name.
    /// </summary>
    /// <param name="categoryName">
    /// Category name for the logger (typically your class name or plugin name).
    /// Appears in log output to identify the source.
    /// </param>
    /// <returns>A logger instance that writes to the application's logging system.</returns>
    /// <remarks>
    /// Use this for debugging, diagnostic messages, and error reporting.
    /// Logs are written to the application's log files automatically.
    /// </remarks>
    /// <example>
    /// <code>
    /// var logger = context.CreateLogger("MyPlugin.MyTask");
    /// logger.LogInformation("Task initialized successfully");
    /// logger.LogWarning("Found {Count} suspicious patterns", count);
    /// </code>
    /// </example>
    ILogger CreateLogger(string categoryName);
    
    /// <summary>
    /// Creates a typed logger instance for the plugin.
    /// </summary>
    /// <typeparam name="T">The type to use as the logger category (typically your class type).</typeparam>
    /// <returns>A logger instance that writes to the application's logging system.</returns>
    /// <remarks>
    /// This is a convenience method equivalent to <c>CreateLogger(typeof(T).FullName)</c>.
    /// Prefer this when you want the logger category to match your class name.
    /// </remarks>
    /// <example>
    /// <code>
    /// var logger = context.CreateLogger&lt;MyTask&gt;();
    /// logger.LogDebug("Processing URL: {Url}", url);
    /// </code>
    /// </example>
    ILogger<T> CreateLogger<T>();
    
    /// <summary>
    /// Gets the application's service provider for dependency injection.
    /// </summary>
    /// <returns>The service provider containing all registered services.</returns>
    /// <remarks>
    /// <para>
    /// Use this for advanced scenarios where you need to resolve services directly.
    /// Most plugins should use <see cref="GetRepositoryAccessor"/> instead for data access.
    /// </para>
    /// <para>
    /// <b>Common use cases:</b>
    /// - Creating scoped service instances
    /// - Accessing configuration settings
    /// - Advanced dependency resolution
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var serviceProvider = context.GetServiceProvider();
    /// using var scope = serviceProvider.CreateScope();
    /// var myService = scope.ServiceProvider.GetRequiredService&lt;IMyService&gt;();
    /// </code>
    /// </example>
    IServiceProvider GetServiceProvider();
    
    /// <summary>
    /// Gets the repository accessor for querying crawled URLs and redirects.
    /// </summary>
    /// <returns>
    /// A repository accessor that provides simple, reflection-free access to crawled data.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Use this instead of the service provider when you need to:
    /// - Check if URLs exist in the crawl
    /// - Validate canonical targets
    /// - Detect duplicate content
    /// - Analyze redirect chains
    /// - Get URL status codes and metadata
    /// </para>
    /// <para>
    /// The repository accessor eliminates the need for reflection-based repository access patterns
    /// that were common in older plugins.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public void Initialize(IHostContext context)
    /// {
    ///     var logger = context.CreateLogger&lt;MyTask&gt;();
    ///     var accessor = context.GetRepositoryAccessor();
    ///     
    ///     context.RegisterTask(new MyTask(logger, accessor));
    /// }
    /// 
    /// // In your task:
    /// public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    /// {
    ///     var canonicalUrl = await _accessor.GetUrlByAddressAsync(
    ///         ctx.Project.ProjectId, 
    ///         canonical);
    ///         
    ///     if (canonicalUrl == null)
    ///     {
    ///         // Report finding: canonical target not crawled
    ///     }
    /// }
    /// </code>
    /// </example>
    IRepositoryAccessor GetRepositoryAccessor();
}

