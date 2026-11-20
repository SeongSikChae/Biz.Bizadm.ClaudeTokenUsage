using Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Storage;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Services
{
    /// <summary>
    /// 롤업 재계산·보관 삭제. 시작할 때 멈춰 있던 동안 밀린 처리를 먼저 끝내고(서버가 요청을 받기 전), 그 뒤 1분마다 한다.
    /// 실패하면 로그만 남기고 다음 주기에 다시 한다.
    /// </summary>
    public sealed class UsageMaintenanceService : BackgroundService
    {
        public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        private readonly DuckDbUsageStore store;
        private readonly TimeProvider timeProvider;
        private readonly ILogger<UsageMaintenanceService> logger;

        public UsageMaintenanceService(DuckDbUsageStore store, TimeProvider timeProvider, ILogger<UsageMaintenanceService> logger)
        {
            this.store = store;
            this.timeProvider = timeProvider;
            this.logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await RunOnceAsync(cancellationToken);
            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using PeriodicTimer timer = new(Interval, timeProvider);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                    await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        private async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            try
            {
                await store.RunMaintenanceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Usage maintenance failed; retrying on the next interval.");
            }
        }
    }
}
