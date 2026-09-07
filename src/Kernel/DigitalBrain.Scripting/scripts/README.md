# The default brain

`start.cs` is a file application with immutable `activities.cs` and `ui.cs`
children. Changing either child changes the effective startup revision.

`activities.cs` connects ordinary neuron APIs:

```text
execution --ActivityExecutionChanged--> activities --ActivityChanged--> desk
```

The runtime records causal execution facts. The `execution` neuron durably queues
and publishes them on a separate turn, allowing UI subscribers to observe the
operations that created them. The `activities` neuron maintains named activity
state. `desk` receives those updates through its subscription and persists the
Home scene and its activity list. Removing that subscription stops renderer
updates; the UI does not bypass it by reading the collector directly.

`ui.cs` composes Home from supported UI primitives. The default is a resizable
graph/chat split, with the activity list at the graph's upper right and voice
enabled. The component tree controls pane order, split ratio, initial graph
scope, activity-list visibility, and input name. Flutter implements the actual
primitives; a new primitive kind still needs a renderer implementation.

Every new input starts an activity with its own correlation. Selecting an
activity shows its causal graph and retained messages/card references. **All**
shows the union of visible activities; **System** shows the subscription graph.
Application source is validated and retained before activation, so editing a
working file does not silently replace the active revision.

Activity facts preserve owner and principal scope. Their own delivery is marked
as telemetry so observing activity does not recursively create more activity.
Failed observer delivery stays queued for retry. A persistently failing observer
pauses later projection in that owner's queue; ordinary business input continues.

Activity summaries and journals are retained with bounds. The UI can reload
retained results after reconnect; this is not an unbounded conversation archive.
