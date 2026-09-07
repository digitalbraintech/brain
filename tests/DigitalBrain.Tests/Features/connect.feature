Feature: Connect
  Connect creates a synapse on the source. Disconnect removes it. Neurons exist when named.

  Scenario: Connect twice is one synapse
    Given a running brain
    When "git" is connected to "run-tests" for "Note"
    And "git" is connected to "run-tests" for "Note"
    Then "git" has 1 synapses

  Scenario: Disconnect removes the synapse and a second disconnect is a no-op
    Given a running brain
    And "git" is connected to "run-tests" for "Note"
    When "git" is disconnected from "run-tests" for "Note"
    And "git" is disconnected from "run-tests" for "Note"
    Then "git" has 0 synapses

  Scenario: After disconnect a fire no longer reaches the old target
    Given a running brain
    And "git" is connected to "run-tests" for "Note"
    And "git" is disconnected from "run-tests" for "Note"
    When "git" fires "Note" {"text":"gone"}
    Then the fire reached 0 neurons
    And "run-tests" incoming journal is empty

  Scenario: A neuron that was only ever named reads as empty
    Given a running brain
    Then "never-touched" has 0 synapses
    And "never-touched" incoming journal is empty
    And "never-touched" state is empty
