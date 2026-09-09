Feature: Journal versus snapshot versus durable anatomy
  Three stores, three jobs. Mixing them is how charts explode and how Orleans
  journaling preview (DurableGrain operation log) gets blamed for domain size.

  * Traffic journal (neuron only): bounded SignalDelivery window — who fired what,
    when, with which correlation. Compacted to 512 entries / 512 KB.
  * Synapses (on the source neuron, IDurableDictionary): durable routing anatomy,
    not a log. Weight, fire count, last fired. Not sequence-numbered.
  * Entity snapshot (IPersistentState): current value. Chart points, profile bio.
    No journal, no synapses, never a graph endpoint.
  * Orleans.Journaling DurableGrain: infrastructure replay of collection ops.
    Keep neuron durable collections small. Put growing values on entities.

  Rule: Writes journal; reads of entities do not

    Scenario: A neuron fire that saves an entity journals the fire, not the snapshot reads
      Given a running brain
      And profile "elon" exists
      And timeline "alice" can handle NewPost
      When account "elon" fires NewPost "bio update" at timeline "alice"
      And profile "elon" is saved with bio "mars"
      Then timeline "alice" incoming journal contains NewPost "bio update"
      And reading profile "elon" returns bio "mars"
      And profile "elon" has no traffic journal

  Rule: Synapses are anatomy living next to the journal, not inside it

    Scenario: Learning a route does not add a synapse record to the traffic journal
      Given a running brain
      And timeline "alice" can handle NewPost
      When account "elon" fires NewPost "hello" at timeline "alice"
      Then account "elon" has a learned NewPost synapse to timeline "alice"
      And account "elon" outgoing journal contains NewPost "hello"
      And account "elon" synapse count is 1
      And account "elon" outgoing journal count is 1
