Feature: Fire
  A neuron emits a signal. It travels along every synapse of that signal type on the
  emitter, or along exactly one synapse when a target is named. Nothing else routes.

  Rule: Fire without a target follows every synapse of that type

    Scenario: Two connected neurons receive, an unconnected one does not
      Given a running brain
      And "elon" is connected to "alice" for "Post"
      And "elon" is connected to "bob" for "Post"
      When "elon" fires "Post" {"text":"starship"}
      Then the fire reached 2 neurons
      And "alice" incoming journal contains "Post" {"text":"starship"}
      And "bob" incoming journal contains "Post" {"text":"starship"}
      And "carol" incoming journal is empty

    Scenario: Synapses of another type do not carry the signal
      Given a running brain
      And "elon" is connected to "alice" for "Post"
      When "elon" fires "Note" {"text":"private"}
      Then the fire reached 0 neurons
      And "alice" incoming journal is empty

  Rule: Fire with a target follows exactly that synapse and creates it

    Scenario: A directed fire reaches only the target and leaves the synapse behind
      Given a running brain
      And "elon" is connected to "alice" for "Post"
      When "elon" fires "Post" {"text":"hi bob"} at "bob"
      Then the fire reached 1 neurons
      And "bob" incoming journal contains "Post" {"text":"hi bob"}
      And "alice" incoming journal is empty
      And "elon" has a synapse to "bob" for "Post"

  Rule: The emitter never receives its own fire

    Scenario: A neuron connected to itself is skipped
      Given a running brain
      And "elon" is connected to "elon" for "Post"
      When "elon" fires "Post" {"text":"echo"}
      Then the fire reached 0 neurons
      And "elon" incoming journal is empty

    Scenario: A directed fire at itself is rejected
      Given a running brain
      When "elon" fires "Post" {"text":"me"} at "elon"
      Then the fire was rejected with a message containing "cannot fire at itself"

  Rule: Both ends journal the envelope

    Scenario: The outgoing and incoming entries share signal id and correlation
      Given a running brain
      When "elon" fires "Post" {"text":"one"} at "alice"
      Then "elon" outgoing journal has 1 entries
      And "alice" incoming journal has 1 entries
      And the latest "alice" incoming entry has source "elon"
      And the latest "alice" incoming entry has the same signal id and correlation as the latest "elon" outgoing entry
