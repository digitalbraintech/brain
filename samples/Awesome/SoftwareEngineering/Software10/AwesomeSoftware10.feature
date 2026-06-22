Feature: Awesome Software Engineering - Software10 (old team)
  Legacy software team using traditional practices. They can still create simple apps via the neuron system.

  Scenario: Software10 team creates a simple todo console app
    Given a software10 team neuron "team10"
    When I send create simple app request "todo list manager" for team "software10"
    Then the timeline contains a SimpleAppCreated

  Scenario: Software10 team creates a basic calculator
    Given a software10 team neuron "team10-calc"
    When I send create simple app request "basic calculator" for team "software10"
    Then the timeline contains a SimpleAppCreated
