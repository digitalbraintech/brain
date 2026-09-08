Feature: React
  Deliver accepts. A neuron reacts to its inbox in its own turns, in order, at least once
  (a failed or crashed reaction is retried from the cursor), and may fire back at the neuron
  that delivered to it.

  Scenario: An echo neuron replies to its source from inside its reaction
    Given a running brain
    When "claude" fires "Ping" {"n":1} at echo "e"
    And "claude" waits up to 5 seconds for an incoming "Pong"
    Then "claude" incoming journal contains "Pong" {"n":1}
    And the latest "claude" incoming entry has the same correlation as the latest "claude" outgoing entry

  Scenario: Reactions run in journal order
    Given a running brain
    When "claude" fires "Ping" {"n":1} at echo "e"
    And "claude" fires "Ping" {"n":2} at echo "e"
    And "claude" fires "Ping" {"n":3} at echo "e"
    And "claude" waits up to 5 seconds for 3 incoming "Pong"
    Then "claude" incoming "Pong" bodies are {"n":1}, {"n":2}, {"n":3}

  Scenario: A reaction that throws is retried on the next wake and nothing is lost
    Given a running brain
    And flaky "f" fails its first reaction
    When "claude" fires "Ping" {"n":1} at flaky "f"
    And "claude" fires "Ping" {"n":2} at flaky "f"
    And "claude" waits up to 10 seconds for 2 incoming "Pong"
    Then flaky "f" reacted 2 times to sequence 1
    And "claude" incoming "Pong" bodies are {"n":1}, {"n":2}

  Scenario: Unreacted entries survive a restart
    Given a running brain with durable storage
    And sleepy "s" is asleep
    When "claude" fires "Ping" {"n":1} at sleepy "s"
    And the silo restarts
    And sleepy "s" is awake
    And "claude" waits up to 10 seconds for an incoming "Pong"
    Then "claude" incoming journal contains "Pong" {"n":1}

  Scenario: Fire returns once the target accepted, before it reacted
    Given a running brain
    And sleepy "s" is asleep
    When "claude" fires "Ping" {"n":1} at sleepy "s"
    Then the fire reached 1 neurons
    And sleepy "s" incoming journal contains "Ping" {"n":1}
    And "claude" incoming journal is empty
