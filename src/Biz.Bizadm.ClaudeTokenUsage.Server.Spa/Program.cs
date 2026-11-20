using CommandLine;
using Microsoft.Extensions.FileProviders;
using System.Configuration;
using Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Storage;
using Biz.Bizadm.ClaudeTokenUsage.Server.Core;
using Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Services;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Spa
{
    public class Program
    {
        public sealed class CmdMain
        {
            [Option("config", Required = true, HelpText = "config file path")]
            public string ConfigFilePath { get; set; } = null!;

            [Option("log", Required = true, HelpText = "log dir path")]
            public string LogDirPath { get; set; } = null!;
        }

        static async Task<int> Main(string[] args)
        {
            ParserResult<CmdMain> result = Parser.Default.ParseArguments<CmdMain>(args);
            return (int)await result.MapResult(
                cmdMain => RunAsync(args, cmdMain),
                errors => Task.FromResult(errors.IsHelp() || errors.IsVersion()
                    ? ExitCode.Success
                    : ExitCode.InvalidArguments));
        }

        private static async Task<ExitCode> RunAsync(string[] args, CmdMain cmdMain)
        {
            AppConfiguration? appConfiguration;
            try
            {
                YamlDotNet.Serialization.Deserializer deserializer = new YamlDotNet.Serialization.Deserializer();
                appConfiguration = deserializer.Deserialize<AppConfiguration>(File.ReadAllText(cmdMain.ConfigFilePath));
                ConfigurationValidator.Validate(appConfiguration);
                KmsSecretProtector.Protect(appConfiguration.OsKms, appConfiguration.ContentRootPath, appConfiguration.Tls,
                    () => File.WriteAllText(cmdMain.ConfigFilePath, new YamlDotNet.Serialization.Serializer().Serialize(appConfiguration)));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                return ExitCode.ConfigurationError;
            }

            WebApplication app;
            try
            {
                app = CreateWebApplication(args, appConfiguration, cmdMain.LogDirPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Host creation error: {ex}");
                return ExitCode.RuntimeError;
            }

            using (app)
            {
                ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();
                try
                {
                    await app.StartAsync();
                    await app.WaitForShutdownAsync();
                    return ExitCode.Success;
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "WebApp terminated unexpectedly");
                    return ExitCode.RuntimeError;
                }
            }
        }

        /// <summary>
        /// 설정으로 서비스와 HTTP 파이프라인을 구성한 앱을 만든다.
        /// 테스트는 <paramref name="configureBuilder"/>로 <c>UseTestServer</c>를 지정한다.
        /// </summary>
        public static WebApplication CreateWebApplication(string[] args, AppConfiguration appConfiguration, string logDirPath,
            Action<WebApplicationBuilder>? configureBuilder = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = appConfiguration.ContentRootPath,
                WebRootPath = Path.Combine(appConfiguration.ContentRootPath, appConfiguration.WebRootPath),
            });

            builder.Logging.AddServerSerilog(logDirPath, "Biz.Bizadm.ClaudeTokenUsage.Server.Spa.log");

            builder.WebHost.ConfigureServerKestrel(appConfiguration.Port!.Value, appConfiguration.Tls, appConfiguration.ContentRootPath);

            bool useTls = appConfiguration.Tls?.UseTls == true;

            builder.Services.AddProblemDetails();
            builder.Services.AddHealthChecks();
            // 진입 어셈블리가 다른 경우(TestServer 등)에도 이 어셈블리의 컨트롤러를 찾도록 명시한다.
            builder.Services.AddControllers().AddApplicationPart(typeof(Program).Assembly);
            builder.Services.AddOpenApi();

            // Windows 서비스·systemd로 실행될 때 서비스 관리자의 시작·중지와 연동한다. 그 밖의 실행에서는 아무것도 하지 않는다.
            // Host.UseWindowsService()는 콘텐츠 루트를 실행 파일 폴더로 바꾸려 해 YAML ContentRootPath와 충돌하므로 서비스 등록 형태를 쓴다.
            builder.Services.AddWindowsService();
            builder.Services.AddSystemd();

            string databaseFilePath = Path.Combine(appConfiguration.ContentRootPath, appConfiguration.DatabaseFilePath);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton(services => DuckDbUsageStore.Open(databaseFilePath, services.GetRequiredService<TimeProvider>()));
            builder.Services.AddHostedService<UsageMaintenanceService>();
            if (useTls)
            {
                builder.Services.AddHsts(configureOptions =>
                {
                    configureOptions.Preload = false;
                    configureOptions.IncludeSubDomains = true;
                    configureOptions.MaxAge = TimeSpan.FromDays(365);
                });
            }

            configureBuilder?.Invoke(builder);
            WebApplication app = builder.Build();

            // 예외 처리를 맨 앞에 두어 이후 모든 예외를 ProblemDetails로 응답한다.
            app.UseExceptionHandler();
            app.UseStatusCodePages();
            if (useTls)
                app.UseHsts();

            // 개발 모드는 SpaProxy가 개발 서버로 넘기므로, 빌드 결과는 운영 모드에서만 제공한다.
            bool isDevelopment = app.Environment.IsDevelopment();
            if (!isDevelopment)
                app.UseStaticFiles();

            app.UseAuthorization();

            app.MapHealthChecks("/health");
            if (isDevelopment)
                app.MapOpenApi().AllowAnonymous();
            app.MapControllers();

            if (!isDevelopment)
                app.MapFallback(context => ServeIndexHtmlAsync(context, app.Environment.WebRootFileProvider));

            return app;
        }

        /// <summary>
        /// 클라이언트 라우팅 fallback. HTML을 요청한 GET에만 index.html을 반환하고, 그 외는 404로 두어
        /// UseStatusCodePages가 ProblemDetails로 응답하게 한다(API 접두사와 무관하게 없는 API 경로가 200이 되지 않음).
        /// </summary>
        private static async Task ServeIndexHtmlAsync(HttpContext context, IFileProvider webRoot)
        {
            bool wantsHtml = HttpMethods.IsGet(context.Request.Method)
                && context.Request.GetTypedHeaders().Accept.Any(accept => accept.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase));
            IFileInfo index = webRoot.GetFileInfo("index.html");
            if (!wantsHtml || !index.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(index);
        }
    }
}
