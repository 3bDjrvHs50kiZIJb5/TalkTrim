using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using TalkTrim.SeedData;
using TalkTrim.Services;
using TalkTrim.Services.DashScope;
using NeoAdmin.Blazor.Extensions;
using NeoAdmin.Blazor.Components;
using TalkTrim.Jobs;
using NeoUI.Blazor.Extensions;
using NeoUI.Blazor.Primitives.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.AddNeoAdminSerilog();

builder.Services.AddNeoUIPrimitives();
builder.Services.AddNeoUIComponents();
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddNeoAdmin(builder.Configuration, options =>
{
    options.SchedulerAssemblies = [Assembly.GetExecutingAssembly()];
});
builder.Services.AddNeoAdminApi(Assembly.GetExecutingAssembly());
builder.Services.Configure<OssOptions>(builder.Configuration.GetSection(OssOptions.SectionName));
builder.Services.Configure<DashScopeOptions>(builder.Configuration.GetSection(DashScopeOptions.SectionName));
builder.Services.AddSingleton<OssUploadService>();
builder.Services.AddHttpClient<ParaformerAsrService>();
builder.Services.AddHttpClient<SubtitleTranslationService>();
builder.Services.AddHttpClient<VideoAsrAudioPrepService>(client =>
{
    client.Timeout = TimeSpan.FromHours(2);
});
builder.Services.AddScoped<VideoTranscriptionService>();
builder.Services.AddScoped<VideoEncodeService>();
builder.Services.AddScoped<CurrentUserRoleService>();
builder.Services.AddSingleton<ProjectJobQueue>();
builder.Services.AddSingleton<ProjectJobCancellationRegistry>();
builder.Services.AddScoped<ProjectJobService>();
builder.Services.AddScoped<ProjectJobExecutor>();
builder.Services.AddHostedService<ProjectJobBackgroundWorker>();

// 无 wwwroot 时 WebRootPath 为 null，FileService 上传会 Path.Combine 失败
var wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(Path.Combine(wwwrootPath, "uploads"));
builder.WebHost.UseWebRoot(wwwrootPath);

var app = builder.Build();

var logsDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
app.Logger.LogInformation(
    "TalkTrim 启动。Environment={Environment}, LogsDirectory={LogsDirectory}",
    app.Environment.EnvironmentName,
    logsDirectory);

DataSetup.Initialize(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseNeoAdminSerilogRequestLogging();
app.UseHttpsRedirection();
// 运行时上传的文件不在 StaticAssets 清单内，需用 StaticFiles 提供
app.UseStaticFiles();
app.MapStaticAssets();
app.UseNeoAdmin();
app.UseAntiforgery();
app.MapRazorPages();
app.MapRazorComponents<TalkTrim.App>()
    .AddAdditionalAssemblies(typeof(LayoutAdmin).Assembly)
    .AddInteractiveServerRenderMode();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()?.Addresses;
    if (addresses is null || addresses.Count == 0)
        return;

    foreach (var address in addresses)
        app.Logger.LogInformation("请在浏览器中访问: {Address}", address);
});

try
{
    app.Logger.LogInformation("TalkTrim 启动完成，开始监听请求。");
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
