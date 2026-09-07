Feature: State
  A neuron remembers the latest signal of each type, durably, outside the window.
  A Neuron<TState> may additionally keep a snapshot; it never appears in a journal.

  Scenario: Firing a signal makes it the latest of its type
    Given a running brain
    When "claude" fires "Note" {"text":"run tests before commit"} at "run-tests"
    Then "run-tests" latest "Note" is {"text":"run tests before commit"}

  Scenario: A second signal of the same type replaces the latest and the journal keeps both
    Given a running brain
    When "claude" fires "Note" {"text":"v1"} at "run-tests"
    And "claude" fires "Note" {"text":"v2"} at "run-tests"
    Then "run-tests" latest "Note" is {"text":"v2"}
    And "run-tests" incoming journal contains "Note" {"text":"v1"}
    And "run-tests" state has 1 entries

  Scenario: Different types are remembered side by side
    Given a running brain
    When "claude" fires "Note" {"text":"x"} at "run-tests"
    And "claude" fires "Confirmed" {} at "run-tests"
    Then "run-tests" state has 2 entries
    And "run-tests" incoming tally for "Confirmed" is 1

  Scenario: Latest survives restart even after the window turned over
    Given a running brain with durable storage
    When "claude" fires "Note" {"text":"keep me"} at "run-tests"
    And "claude" fires "Tick" {} at "run-tests" 600 times
    And the silo restarts
    Then "run-tests" latest "Note" is {"text":"keep me"}

  Scenario: A snapshot neuron saves on receive and the snapshot is not in any journal
    Given a running brain with durable storage
    When "claude" fires "SetBio" {"bio":"mars"} at profile "elon"
    And the silo restarts
    Then profile "elon" bio is "mars"
    And profile "elon" incoming journal contains "SetBio" {"bio":"mars"}
    And profile "elon" outgoing journal has 0 entries
