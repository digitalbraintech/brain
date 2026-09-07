Feature: Read
  Read is a query over state, synapses and journals. It changes nothing. With a timeout it
  waits for the next journal entry. The caller is a Session neuron named by its principal.

  Scenario: A directed fire from a session leaves a synapse and both journals
    Given a running brain
    When session "claude" fires "Note" {"text":"run tests"} at "run-tests"
    Then the fire result reports 1 delivered
    And reading "claude" shows a synapse to "run-tests" for "Note"
    And reading "run-tests" shows latest "Note" {"text":"run tests"}

  Scenario: The default read returns all four views
    Given a running brain
    When session "claude" fires "Note" {"text":"x"} at "run-tests"
    And "run-tests" is read
    Then the read has state, synapses, incoming and outgoing

  Scenario: A partial read returns only what was asked
    Given a running brain
    When "run-tests" is read for "synapses"
    Then the read has only synapses

  Scenario: Reading changes nothing
    Given a running brain
    When session "claude" fires "Note" {"text":"x"} at "run-tests"
    And "run-tests" is read 5 times
    And "claude" is read 5 times
    Then "run-tests" incoming journal has 1 entries
    And "claude" outgoing journal has 1 entries
    And "claude" has 1 synapses

  Scenario: A journal read numbers its window and reports the running total
    Given a running brain
    When session "claude" fires "Note" {"text":"x"} at "run-tests" 2 times
    And session "elon" fires "Note" {"text":"y"} at "run-tests"
    And "run-tests" is read for "incoming"
    Then the read incoming sequences are "1, 2, 3"
    And the read incoming total recorded is 3

  Scenario: A walk from a topic reaches exactly its targets
    Given a running brain
    And "git" is connected to "run-tests" for "Note"
    And "git" is connected to "small-commits" for "Note"
    And "git" is connected to "unrelated" for "Other"
    When the synapses of "git" for "Note" are followed
    Then the walk visited "run-tests, small-commits"

  Scenario: A read with timeout returns when a new entry arrives
    Given a running brain
    When "claude" incoming is read after 0 with a 5 second timeout while "elon" fires "Ping" {} at "claude" after 300 ms
    Then the timed read returned 1 entries

  Scenario: A read with timeout returns empty at the deadline
    Given a running brain
    When "claude" incoming is read after 0 with a 1 second timeout
    Then the timed read returned 0 entries

  Scenario: Two connections with the same principal share one Session neuron
    Given a running brain
    When session "claude" fires "Note" {"text":"a"} at "one"
    And session "claude" fires "Note" {"text":"b"} at "two"
    Then "claude" outgoing journal has 2 entries
    And "claude" has 2 synapses

  Scenario: Two principals fire from their own Session neurons
    Given a running brain
    When session "alice" fires "Note" {} at "one"
    And session "bob" fires "Note" {} at "two"
    Then "alice" has 1 synapses
    And "bob" has 1 synapses
    And "alice" outgoing journal has 1 entries
