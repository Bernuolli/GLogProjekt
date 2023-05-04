using Timer = System.Timers.Timer;

namespace GriffSoft.GLog.WorkerService.Logic.AlertScheduler
{
    public class GLogTask
    {
        public GLogTask( List<GLogTask> parent, TimeSpan span, Func<Task> taskFunc )
        {
            Timer timer = new( span.TotalMilliseconds );
            timer.Elapsed += ( _,_ ) =>
            {
                timer.Stop();
                taskFunc.Invoke();
                parent.RemoveTaskFromList( this );
            };
            timer.Start();
        }
    }
}
