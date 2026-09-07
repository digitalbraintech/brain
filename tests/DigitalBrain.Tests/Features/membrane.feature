Feature: Membrane
  A signal is rejected before delivery when its body exceeds 64 KB or its type is not vocabulary.
  A rejected fire leaves no trace on either end.

  Scenario: A body over 64 KB is rejected with advice
    Given a running brain
    When session "claude" fires "Note" with a body of 70000 bytes at "big"
    Then the operation failed with a message containing "limit is 64 KB"
    And "big" incoming journal is empty
    And "claude" outgoing journal has 0 entries

  Scenario: A type with digits or spaces is rejected with advice
    Given a running brain
    When session "claude" fires "note-2026-09-08" {"text":"x"} at "dated"
    Then the operation failed with a message containing "letters only"
    And "dated" incoming journal is empty

  Scenario: A body that is not JSON is rejected
    Given a running brain
    When session "claude" fires "Note" not-json at "bad"
    Then the operation failed with a message containing "not valid JSON"

  Scenario: An empty body is allowed and stored as an empty object
    Given a running brain
    When session "claude" fires "Confirmed" with an empty body at "run-tests"
    Then "run-tests" latest "Confirmed" is {}

  Scenario: A neuron remembers at most 256 signal types
    Given a running brain
    When session "claude" fires 256 distinct types at "vocab"
    And session "claude" fires one more distinct type at "vocab"
    Then the operation failed with a message containing "vocabulary"
    And "vocab" state has 256 entries
