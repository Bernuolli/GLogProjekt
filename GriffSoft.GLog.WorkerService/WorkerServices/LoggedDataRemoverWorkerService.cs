using GriffSoft.GLog.WorkerService.Helpers;
using GriffSoft.GLog.WorkerService.Logic.Services.Contracts;
using GriffSoft.GLog.WorkerService.Shared.Models;
using GriffSoft.GLog.WorkerService.Shared.Extensions;
using GriffSoft.GLog.WorkerService.Shared.Queue;
using Microsoft.Extensions.Options;
using GriffSoft.GLog.WorkerService.Logic.Services;

namespace GriffSoft.GLog.WorkerService.WorkerServices
{
    public class LoggedDataRemoverWorkerService : BackgroundService
    {
        private readonly ILogger<LoggedDataRemoverWorkerService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IGLogWorkerErrorHelper _errorHelper;
        private readonly IOptions<GLogConfig> _config;
        private readonly IBackGroundQueue<FeedbackData> _feedbackQueue;
        private readonly int _minutesDelay = 1;

        public LoggedDataRemoverWorkerService( ILogger<LoggedDataRemoverWorkerService> logger, IServiceScopeFactory scopeFactory,
            IGLogWorkerErrorHelper errorHelper, IOptions<GLogConfig> config, IBackGroundQueue<FeedbackData> feedbackQueue )
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _errorHelper = errorHelper;
            _config = config;
            _feedbackQueue = feedbackQueue;
        }

        protected async override Task ExecuteAsync( CancellationToken stoppingToken )
        {
            while( !stoppingToken.IsCancellationRequested )
            {
                try
                {
                    await RunLoggedDataRemover();
                    await Task.Delay( TimeSpan.FromMinutes( _minutesDelay ), stoppingToken );
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

        /// <summary>
        /// Go through <see cref="_feedbackQueue"/> and call <see cref="IGLogLoggedDataRemoverService.RemoveLoggedData"/>
        /// if needed.
        /// </summary>
        /// <returns></returns>
        private async Task RunLoggedDataRemover()
        {
            _logger.LogDebugWithLevelCheck( $"{nameof( RunLoggedDataRemover )} is started" );

            using var scope = _scopeFactory.CreateScope();
            var loggedDataRemoverService = scope.ServiceProvider.GetRequiredService<IGLogLoggedDataRemoverService>();

            List<RemoveLoggedDataParam> listForRemove = new();

            while( !_feedbackQueue.IsEmpty() )
            {
                FeedbackData? feedbackData = _feedbackQueue.Dequeue();
                bool deleteOriginal = _config.Value.LogTransferProcessMode == TransferProcessMode.Move;
                if( feedbackData == null )
                {
                    continue;
                }
                if( feedbackData.BrokerDataSource == Logic.Forras.Types.BrokerDataSource.FUVLog && !deleteOriginal )
                {
                    continue;
                }              
               listForRemove.Add(new RemoveLoggedDataParam(feedbackData.GlogBrokerDataId, feedbackData.ExternalId, feedbackData.BrokerDataSource, deleteOriginal));
            }

            await loggedDataRemoverService.RemoveLoggedDataFromList(listForRemove);
            _logger.LogDebugWithLevelCheck( $"{nameof( RunLoggedDataRemover )} is finished" );
        }
    }
}
