using GriffSoft.GLog.WorkerService.Shared;

namespace GriffSoft.GLog.WorkerService.Logic.Forras.Services.Contracts
{
    public interface IFuvConverterService
    {
        /// <summary>
        /// Get fuv log data and convert them to <see cref="LogEntryDto"/>.
        /// </summary>
        /// <param name="id">Ids of log data to convert.</param>
        /// <returns></returns>
        Task<List<LogEntryDto>> MakeLogEntryDtos( List<int> id );
    }
}
