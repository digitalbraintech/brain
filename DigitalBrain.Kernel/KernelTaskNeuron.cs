using DigitalBrain.Core;
using Microsoft.Extensions.AI;
namespace DigitalBrain.Kernel;

[GrainType("kernel.task.v1")]
public class KernelTaskNeuron : Neuron, IKernelTask
{
    public KernelTaskNeuron(ILogger<KernelTaskNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task HandleAsync(RunTask cmd)
    {
        await FireAsync(new TaskCreated(cmd.TaskId, cmd.Description));
        await FireAsync(new TaskStarted(cmd.TaskId));
        await FireAsync(new TaskProgress(cmd.TaskId, "planning"));
        string result;
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat != null)
        {
            await FireAsync(new TaskProgress(cmd.TaskId, "running-llm"));
            var prompt = $"Perform the task and output ONLY the concise result value: {cmd.Description}";
            var response = await chat.GetResponseAsync(prompt);
            result = response.Text.Trim();
            if (string.IsNullOrWhiteSpace(result)) result = "completed:" + cmd.Description;
        }
        else
        {
            await FireAsync(new TaskProgress(cmd.TaskId, "running-fallback"));
            result = "completed-no-llm:" + cmd.Description;
        }
        await FireAsync(new TaskProgress(cmd.TaskId, "finalizing"));
        await FireAsync(new TaskCompleted(cmd.TaskId, result));

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus != null)
        {
            var recent = OutgoingJournal.Concat(IncomingJournal).ToList();
            var tm = UiSurfaceLiveData.TaskManagerFromTasks(recent, userId: cmd.UserId, sessionId: cmd.SessionId);
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(tm, Self.Value));

            var directData = System.Text.Json.JsonSerializer.Serialize(new
            {
                totals = tm.Props.GetValueOrDefault("totals"),
                tasks = tm.Props.GetValueOrDefault("tasks")
            });
            bus.Broadcast(new RfwCard("digitalbrain", "TaskManagerCard", directData));
        }
    }

    public async Task HandleAsync(CancelTask cmd)
    {
        await FireAsync(new TaskCancelled(cmd.TaskId));

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus != null)
        {
            var recent = OutgoingJournal.Concat(IncomingJournal).ToList();
            var tm = UiSurfaceLiveData.TaskManagerFromTasks(recent, userId: cmd.UserId, sessionId: cmd.SessionId);
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(tm, Self.Value));

            var directData = System.Text.Json.JsonSerializer.Serialize(new
            {
                totals = tm.Props.GetValueOrDefault("totals"),
                tasks = tm.Props.GetValueOrDefault("tasks")
            });
            bus.Broadcast(new RfwCard("digitalbrain", "TaskManagerCard", directData));
        }
    }

    public Task<TaskInfo> GetInfoAsync()
    {
        var history = OutgoingJournal.Concat(IncomingJournal).ToList();
        var completed = history.OfType<TaskCompleted>().LastOrDefault();
        if (completed != null)
            return Task.FromResult(new TaskInfo(completed.TaskId, "completed", completed.Result));
        var cancelled = history.OfType<TaskCancelled>().LastOrDefault();
        if (cancelled != null)
            return Task.FromResult(new TaskInfo(cancelled.TaskId, "cancelled", null));
        var progress = history.OfType<TaskProgress>().LastOrDefault();
        if (progress != null)
            return Task.FromResult(new TaskInfo(progress.TaskId, "running:" + progress.Detail, null));
        var started = history.OfType<TaskStarted>().LastOrDefault();
        if (started != null)
            return Task.FromResult(new TaskInfo(started.TaskId, "running", null));
        var created = history.OfType<TaskCreated>().LastOrDefault();
        if (created != null)
            return Task.FromResult(new TaskInfo(created.TaskId, "created", null));
        var id = this.GetPrimaryKeyString() ?? "task";
        TaskId idTask = id;
        return Task.FromResult(new TaskInfo(idTask, "created", null));
    }
}


