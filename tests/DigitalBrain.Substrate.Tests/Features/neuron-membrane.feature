Feature: Neuron membrane — fire, handle, journal
  The outside language is IDigitalBrain. A script, MCP tool, or console fires a Signal
  at a named neuron. There is no IOrleans and no FireSignal on the grain contract:
  NeuronReference.SendAsync is the fire, and it only compiles when the target IHandle<T>s T.
  IHandle<T> is the innate capability to receive T. A successful handle learns a
  synapse on the SOURCE neuron (Hebbian). The traffic journal records the
  interaction envelope (SignalDelivery), not a growing domain snapshot.

  Rule: Directed fire is point-to-point and awaited

    Scenario: A handled fire journals incoming on the receiver and learns a synapse on the source
      Given a running brain
      And timeline "alice" can handle NewPost
      When account "elon" fires NewPost "hello mars" at timeline "alice"
      Then the delivery to timeline "alice" is handled
      And timeline "alice" incoming journal contains NewPost "hello mars"
      And account "elon" has a learned NewPost synapse to timeline "alice"

    Scenario: A fire the receiver cannot handle is still journaled as unhandled
      Given a running brain
      When account "elon" fires NewPost "nobody home" at account "vlad"
      Then the delivery to account "vlad" is unhandled
      And account "vlad" incoming journal contains NewPost "nobody home"
      And account "elon" has no NewPost synapse to account "vlad"

  Rule: What rides on a Signal versus the delivery envelope

    Scenario: The signal is the payload; the journal stores the envelope around it
      Given a running brain
      And timeline "alice" can handle NewPost
      When account "elon" fires NewPost "payload only" at timeline "alice"
      Then the journaled NewPost on timeline "alice" carries caller account "elon"
      And that delivery has a signal id, correlation id, sequence, and timestamp
      And the NewPost payload text is "payload only"
