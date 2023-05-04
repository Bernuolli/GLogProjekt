using GriffSoft.GLog.WorkerService.Logic.Services.Contracts;
using GriffSoft.GLog.WorkerService.Shared.Models;
using GriffSoft.GLog.WorkerService.Shared.Extensions;
using Microsoft.Extensions.Options;

namespace GriffSoft.GLog.WorkerService.WorkerServices
{
    public class LogCleanerWorkerService : BackgroundService
    {
        private readonly ILogger<LogCleanerWorkerService> _logger;
        private readonly int _logStorageDurationInDays;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IGLogWorkerErrorHelper _errorHelper;
        private readonly int _hoursDelay;

        public LogCleanerWorkerService( ILogger<LogCleanerWorkerService> logger, IOptions<GLogConfig> config,
            IServiceScopeFactory scopeFactory, IGLogWorkerErrorHelper errorHelper )
        {
            _logger = logger;
            _logStorageDurationInDays = config.Value.LogStorageDurationInDays;
            _scopeFactory = scopeFactory;
            _hoursDelay = config.Value.RunLogCleanerInHours;
            _errorHelper = errorHelper;
        }

        protected override async Task ExecuteAsync( CancellationToken stoppingToken )
        {
            while( !stoppingToken.IsCancellationRequested )
            {
                try
                {
                    await RunLogCleaner();
                    await Task.Delay( TimeSpan.FromHours( _hoursDelay ), stoppingToken );
                }
                catch( Exception ex )
                {
                    _errorHelper.AddError( out TimeSpan nextTryTimeSpan, out string errorPrefix );
                    _logger.LogError( ex, errorPrefix + ex.Message );
                    await Task.Delay( nextTryTimeSpan, stoppingToken );
                    continue;
                }
            }
        }

        private async Task RunLogCleaner()
        {
            _logger.LogDebugWithLevelCheck( $"{nameof( RunLogCleaner )} is started" );

            using var scope = _scopeFactory.CreateScope();
            var gLogCleanerService = scope.ServiceProvider.GetRequiredService<IGLogCleanerService>();

            await gLogCleanerService.ClearLogData( _logStorageDurationInDays );

            _logger.LogDebugWithLevelCheck( $"{nameof( RunLogCleaner )} is finished" );
        }
    }
}
