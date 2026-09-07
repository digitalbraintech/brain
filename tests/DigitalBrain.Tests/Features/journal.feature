Feature: Journal
  Two bounded windows per neuron. Envelopes only. Tallies and sequences outlive the window.

  Scenario: Tallies count every delivery per type and survive restart
    Given a running brain with durable storage
    When "elon" fires "Post" {"n":1} at "alice"
    And "elon" fires "Post" {"n":2} at "alice"
    And "elon" fires "Note" {"n":3} at "alice"
    And the silo restarts
    Then "alice" incoming tally for "Post" is 2
    And "alice" incoming tally for "Note" is 1
    And "alice" incoming journal has 3 entries

  Scenario: The window drops the oldest entries but keeps the sequence
    Given a running brain
    When "elon" fires "Tick" {} at "alice" 520 times
    Then "alice" incoming journal has 512 entries
    And "alice" incoming last sequence is 520
    And "alice" incoming tally for "Tick" is 520

  Scenario: Reading a journal is not traffic
    Given a running brain
    When "elon" fires "Post" {"n":1} at "alice"
    And "alice" incoming journal is read 3 times
    Then "alice" incoming journal has 1 entries
    And "alice" outgoing journal has 0 entries
    And "elon" outgoing journal has 1 entries
