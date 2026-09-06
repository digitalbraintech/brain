async Task ConfigureActivities()
{
    var execution = Brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
    var activities = Brain.Get<IActivities>(IActivities.DefaultInstanceName);
    await activities.SubscribeToAsync<IActivities, IActivitySource, ActivityExecutionChanged>(execution.Id, CancellationToken);

    var ino = Brain.Get<IAssistant>("assistant");
    var inbox = Brain.Get<IComposer>(IComposer.DefaultInstanceName);
    await ino.SubscribeToAsync<IComposer, UserMessaged>(inbox.Id, CancellationToken);
}
